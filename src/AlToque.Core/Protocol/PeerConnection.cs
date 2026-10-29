using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AlToque.Core.Security;

namespace AlToque.Core.Protocol;

public enum ConnectFailure
{
    /// <summary>No hubo conexión TCP: equipo apagado, otra red o firewall.</summary>
    Unreachable,

    /// <summary>Hubo TCP pero el otro lado cortó el apretón de manos: su política no nos admite.</summary>
    Rejected,

    /// <summary>El otro lado presentó una clave distinta a la esperada.</summary>
    IdentityMismatch,

    /// <summary>El otro lado no habla el protocolo de Al Toque o usa cifrado débil.</summary>
    Incompatible,
}

public sealed class PeerConnectException(ConnectFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ConnectFailure Failure { get; } = failure;
}

/// <summary>Conexión TLS mutua autenticada con claves fijadas. La identidad del otro lado sale del certificado del canal.</summary>
public sealed class PeerConnection : IAsyncDisposable
{
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly SslApplicationProtocol Alpn = new(AppInfo.Alpn);

    private readonly Socket _socket;
    private readonly SslStream _ssl;

    private PeerConnection(Socket socket, SslStream ssl, byte[] remoteSpki)
    {
        _socket = socket;
        _ssl = ssl;
        RemotePublicKey = remoteSpki;
        RemoteDeviceId = PeerKeys.DeviceIdFromSpki(remoteSpki);
        RemoteFingerprint = PeerKeys.Fingerprint(remoteSpki);
        RemoteEndPoint = (IPEndPoint)socket.RemoteEndPoint!;
        LocalEndPoint = (IPEndPoint)socket.LocalEndPoint!;
        Frames = new FrameChannel(ssl);
    }

    public FrameChannel Frames { get; }
    public byte[] RemotePublicKey { get; }
    public byte[] RemoteFingerprint { get; }
    public string RemoteDeviceId { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public IPEndPoint LocalEndPoint { get; }
    public string CipherSuite => _ssl.NegotiatedCipherSuite.ToString();
    public SslProtocols Protocol => _ssl.SslProtocol;

    public static async Task<PeerConnection> ConnectAsync(
        IPEndPoint endPoint, DeviceIdentity identity, Func<byte[], bool> acceptServerKey, CancellationToken ct)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        ConfigureSocket(socket);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(endPoint, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new PeerConnectException(ConnectFailure.Unreachable, "No se pudo conectar.", ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        byte[]? remoteSpki = null;
        var keyRejected = false;
        var stream = new NetworkStream(socket, ownsSocket: true);
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "altoque.local",
            ApplicationProtocols = [Alpn],
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            AllowRenegotiation = false,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            ClientCertificateContext = SslStreamCertificateContext.Create(identity.Certificate, null, offline: true),
            LocalCertificateSelectionCallback = (_, _, _, _, _) => identity.Certificate,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                remoteSpki = ExtractAcceptableSpki(cert);
                if (remoteSpki is null || !acceptServerKey(remoteSpki))
                {
                    keyRejected = true;
                    return false;
                }
                return true;
            },
        };

        try
        {
            using var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hsCts.CancelAfter(HandshakeTimeout);
            await ssl.AuthenticateAsClientAsync(options, hsCts.Token).ConfigureAwait(false);
            EnsureStrongChannel(ssl);
            return new PeerConnection(socket, ssl, remoteSpki!);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not PeerConnectException)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            if (keyRejected)
                throw new PeerConnectException(ConnectFailure.IdentityMismatch, "La clave del compañero no coincide.", ex);
            throw new PeerConnectException(ConnectFailure.Rejected, "El compañero rechazó la conexión.", ex);
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Completa el lado servidor. Si <paramref name="acceptClientKey"/> rechaza la clave, el apretón de manos
    /// se corta sin revelar nada.
    /// </summary>
    public static async Task<PeerConnection?> AcceptAsync(
        Socket socket, DeviceIdentity identity, SslStreamCertificateContext serverContext, Func<byte[], bool> acceptClientKey, CancellationToken ct)
    {
        ConfigureSocket(socket);
        byte[]? remoteSpki = null;
        var stream = new NetworkStream(socket, ownsSocket: true);
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificateContext = serverContext,
            ClientCertificateRequired = true,
            ApplicationProtocols = [Alpn],
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            AllowRenegotiation = false,
            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                remoteSpki = ExtractAcceptableSpki(cert);
                return remoteSpki is not null && acceptClientKey(remoteSpki);
            },
        };
        try
        {
            using var hsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hsCts.CancelAfter(HandshakeTimeout);
            await ssl.AuthenticateAsServerAsync(options, hsCts.Token).ConfigureAwait(false);
            EnsureStrongChannel(ssl);
            if (remoteSpki is null)
                throw new AuthenticationException("Sin certificado de cliente.");
            return new PeerConnection(socket, ssl, remoteSpki);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static SslStreamCertificateContext CreateServerContext(DeviceIdentity identity) =>
        SslStreamCertificateContext.Create(identity.Certificate, null, offline: true);

    private static void ConfigureSocket(Socket socket)
    {
        socket.NoDelay = true;
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 15);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (SocketException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private static byte[]? ExtractAcceptableSpki(X509Certificate? certificate)
    {
        if (certificate is null)
            return null;
        try
        {
            var cert2 = certificate as X509Certificate2;
            var owned = cert2 is null ? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()) : null;
            try
            {
                var c = cert2 ?? owned!;
                return PeerKeys.IsAcceptableKey(c) ? PeerKeys.ExportSpki(c) : null;
            }
            finally
            {
                owned?.Dispose();
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>ALPN obligatorio y solo cifrados AEAD con intercambio efímero.</summary>
    private static void EnsureStrongChannel(SslStream ssl)
    {
        if (ssl.NegotiatedApplicationProtocol != Alpn)
            throw new PeerConnectException(ConnectFailure.Incompatible, "ALPN no negociado.");
        if (ssl.SslProtocol == SslProtocols.Tls13)
            return;
        var suite = ssl.NegotiatedCipherSuite.ToString();
        var aead = suite.Contains("GCM", StringComparison.Ordinal) || suite.Contains("CHACHA20", StringComparison.Ordinal);
        if (ssl.SslProtocol != SslProtocols.Tls12 || !suite.Contains("ECDHE", StringComparison.Ordinal) || !aead)
            throw new PeerConnectException(ConnectFailure.Incompatible, "Cifrado débil: " + suite);
    }

    public void Abort()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception)
        {
        }
        _socket.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _ssl.ShutdownAsync().WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        await _ssl.DisposeAsync().ConfigureAwait(false);
    }
}
