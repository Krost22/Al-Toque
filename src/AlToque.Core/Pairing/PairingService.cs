using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;

namespace AlToque.Core.Pairing;

public enum PairingOutcome
{
    Paired,

    /// <summary>El usuario de este equipo dijo que el código no coincide.</summary>
    Rejected,

    /// <summary>El otro usuario dijo que no coincide o canceló.</summary>
    RemoteRejected,

    /// <summary>El compromiso no cuadró: posible intermediario.</summary>
    Tampered,

    Failed,
    Timeout,
    Locked,
    NotPairing,
}

/// <summary>Código de 6 dígitos que la interfaz muestra en ambas pantallas para compararlo.</summary>
public sealed class PairingPrompt
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _aborted = new();

    internal PairingPrompt(string peerId, string peerName, string code, bool isInitiator)
    {
        PeerId = peerId;
        PeerName = peerName;
        Code = code;
        IsInitiator = isInitiator;
    }

    public string PeerId { get; }
    public string PeerName { get; }

    /// <summary>Por ejemplo "482 913".</summary>
    public string Code { get; }

    public bool IsInitiator { get; }

    /// <summary>Avatar que mostró el otro equipo en su HELLO (solo decorativo).</summary>
    public string? PeerAvatar { get; init; }

    public string PeerColor => IdentityVisuals.ColorHex(PeerId);
    public string PeerSymbols => IdentityVisuals.SymbolSequence(PeerId);
    public PairingOutcome? Outcome { get; private set; }

    /// <summary>Se cancela si el otro lado se retira antes de que el usuario responda.</summary>
    public CancellationToken Aborted => _aborted.Token;

    public event Action<PairingPrompt>? Completed;

    internal Task<bool> Decision => _decision.Task;

    public void Confirm(bool matches) => _decision.TrySetResult(matches);

    internal void Finish(PairingOutcome outcome)
    {
        if (Outcome is not null)
            return;
        Outcome = outcome;
        _decision.TrySetResult(false);
        try
        {
            _aborted.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        Completed?.Invoke(this);
    }
}

/// <summary>
/// Emparejamiento por comparación numérica con compromiso (sección 12.4). Los números se intercambian dentro del
/// canal TLS y el código incluye las huellas que ve cada lado: un intermediario provoca códigos distintos.
/// </summary>
public sealed class PairingService
{
    public static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UserTimeout = TimeSpan.FromSeconds(90);
    private const int MaxFailures = 3;

    private readonly DeviceIdentity _identity;
    private readonly TrustStore _trust;
    private readonly Func<string> _alias;
    private readonly Func<string?> _avatar;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Lock _lock = new();
    private DateTimeOffset? _windowUntil;
    private DateTimeOffset? _lockedUntil;
    private int _failures;
    private Timer? _windowTimer;

    public PairingService(DeviceIdentity identity, TrustStore trust, Func<string> alias, Func<string?>? avatar = null)
    {
        _identity = identity;
        _trust = trust;
        _alias = alias;
        _avatar = avatar ?? (() => null);
    }

    /// <summary>Se abrió o cerró la ventana de emparejamiento.</summary>
    public event Action? WindowChanged;

    /// <summary>Hay un código que mostrar (como receptor o como iniciador).</summary>
    public event Action<PairingPrompt>? PromptRequested;

    public event Action<string, PairingOutcome>? Finished;

    public bool IsWindowOpen
    {
        get
        {
            lock (_lock)
                return _windowUntil is { } u && u > DateTimeOffset.UtcNow;
        }
    }

    public DateTimeOffset? WindowClosesAt
    {
        get
        {
            lock (_lock)
                return IsWindowOpenLocked() ? _windowUntil : null;
        }
    }

    public bool IsLocked
    {
        get
        {
            lock (_lock)
                return _lockedUntil is { } l && l > DateTimeOffset.UtcNow;
        }
    }

    private bool IsWindowOpenLocked() => _windowUntil is { } u && u > DateTimeOffset.UtcNow;

    public void OpenWindow()
    {
        lock (_lock)
        {
            _windowUntil = DateTimeOffset.UtcNow + WindowDuration;
            _windowTimer?.Dispose();
            _windowTimer = new Timer(_ => CloseWindow(), null, WindowDuration, Timeout.InfiniteTimeSpan);
        }
        WindowChanged?.Invoke();
    }

    public void CloseWindow()
    {
        lock (_lock)
        {
            if (_windowUntil is null)
                return;
            _windowUntil = null;
            _windowTimer?.Dispose();
            _windowTimer = null;
        }
        WindowChanged?.Invoke();
    }

    /// <summary>Lado receptor: alguien se conectó con propósito "pair".</summary>
    internal async Task HandleResponderAsync(PeerConnection conn, HelloMessage hello, CancellationToken ct)
    {
        if (IsLocked)
        {
            await SendErrorAsync(conn, ErrorCodes.PairingLocked, ct).ConfigureAwait(false);
            return;
        }
        if (!IsWindowOpen)
        {
            await SendErrorAsync(conn, ErrorCodes.NotPairing, ct).ConfigureAwait(false);
            return;
        }
        if (!await _busy.WaitAsync(0, ct).ConfigureAwait(false))
        {
            await SendErrorAsync(conn, ErrorCodes.Busy, ct).ConfigureAwait(false);
            return;
        }
        PairingPrompt? prompt = null;
        var outcome = PairingOutcome.Failed;
        try
        {
            await conn.Frames.SendAsync(FrameType.Hello, LocalHello(), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);

            using var commitFrame = await conn.Frames.ReadExpectAsync(StepTimeout, ct, FrameType.PairCommit).ConfigureAwait(false);
            var commit = DecodeHex32(commitFrame.Parse(ProtocolJsonContext.Default.PairCommitMessage).Commit);

            var nB = RandomNumberGenerator.GetBytes(32);
            await conn.Frames.SendAsync(FrameType.PairNonce, new PairNonceMessage { Nonce = Convert.ToHexStringLower(nB) },
                ProtocolJsonContext.Default.PairNonceMessage, ct).ConfigureAwait(false);

            using var revealFrame = await conn.Frames.ReadExpectAsync(StepTimeout, ct, FrameType.PairReveal).ConfigureAwait(false);
            var nA = DecodeHex32(revealFrame.Parse(ProtocolJsonContext.Default.PairRevealMessage).Nonce);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(nA), commit))
            {
                outcome = PairingOutcome.Tampered;
                return;
            }

            var code = ComputeCode(conn.RemoteFingerprint, _identity.Fingerprint, nA, nB);
            prompt = new PairingPrompt(conn.RemoteDeviceId, CleanAlias(hello.Alias), code, isInitiator: false) { PeerAvatar = Capabilities.AvatarOf(hello.Caps) };
            outcome = await ConfirmAsync(conn, prompt, hello, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ProtocolException or IOException or TimeoutException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            outcome = PairingOutcome.Failed;
        }
        finally
        {
            prompt?.Finish(outcome);
            RecordOutcome(outcome);
            _busy.Release();
            Finished?.Invoke(conn.RemoteDeviceId, outcome);
        }
    }

    /// <summary>Lado iniciador: me conecto a un equipo con la ventana abierta.</summary>
    public async Task<PairingOutcome> PairWithAsync(IPEndPoint endPoint, string? expectedDeviceId, CancellationToken ct)
    {
        PeerConnection conn;
        try
        {
            conn = await PeerConnection.ConnectAsync(endPoint, _identity,
                key => expectedDeviceId is null || PeerKeys.DeviceIdFromSpki(key) == expectedDeviceId, ct).ConfigureAwait(false);
        }
        catch (PeerConnectException ex)
        {
            return ex.Failure == ConnectFailure.Rejected ? PairingOutcome.NotPairing : PairingOutcome.Failed;
        }

        PairingPrompt? prompt = null;
        var outcome = PairingOutcome.Failed;
        await using (conn.ConfigureAwait(false))
        {
            try
            {
                Frame helloFrame;
                try
                {
                    await conn.Frames.SendAsync(FrameType.Hello, LocalHello(), ProtocolJsonContext.Default.HelloMessage, ct).ConfigureAwait(false);
                    helloFrame = await conn.Frames.ReadExpectAsync(StepTimeout, ct, FrameType.Hello, FrameType.Error).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or ProtocolException)
                {
                    // En TLS 1.3 el receptor rechaza nuestro certificado justo después del apretón de manos.
                    return outcome = PairingOutcome.NotPairing;
                }
                using var _ = helloFrame;
                if (helloFrame.Type == FrameType.Error)
                {
                    var err = helloFrame.Parse(ProtocolJsonContext.Default.ErrorMessage).Code;
                    return outcome = err switch
                    {
                        ErrorCodes.PairingLocked => PairingOutcome.Locked,
                        ErrorCodes.NotPairing => PairingOutcome.NotPairing,
                        _ => PairingOutcome.Failed,
                    };
                }
                var hello = helloFrame.Parse(ProtocolJsonContext.Default.HelloMessage);
                if (hello.Purpose != Purposes.Pair)
                    throw new ProtocolException("Propósito inesperado.");

                var nA = RandomNumberGenerator.GetBytes(32);
                await conn.Frames.SendAsync(FrameType.PairCommit, new PairCommitMessage { Commit = Convert.ToHexStringLower(SHA256.HashData(nA)) },
                    ProtocolJsonContext.Default.PairCommitMessage, ct).ConfigureAwait(false);
                using var nonceFrame = await conn.Frames.ReadExpectAsync(StepTimeout, ct, FrameType.PairNonce).ConfigureAwait(false);
                var nB = DecodeHex32(nonceFrame.Parse(ProtocolJsonContext.Default.PairNonceMessage).Nonce);
                await conn.Frames.SendAsync(FrameType.PairReveal, new PairRevealMessage { Nonce = Convert.ToHexStringLower(nA) },
                    ProtocolJsonContext.Default.PairRevealMessage, ct).ConfigureAwait(false);

                var code = ComputeCode(_identity.Fingerprint, conn.RemoteFingerprint, nA, nB);
                prompt = new PairingPrompt(conn.RemoteDeviceId, CleanAlias(hello.Alias), code, isInitiator: true) { PeerAvatar = Capabilities.AvatarOf(hello.Caps) };
                outcome = await ConfirmAsync(conn, prompt, hello, ct).ConfigureAwait(false);
                return outcome;
            }
            catch (Exception ex) when (ex is ProtocolException or IOException or TimeoutException or System.Security.Authentication.AuthenticationException)
            {
                return outcome = PairingOutcome.Failed;
            }
            catch (OperationCanceledException)
            {
                return outcome = PairingOutcome.Failed;
            }
            finally
            {
                prompt?.Finish(outcome);
                Finished?.Invoke(conn.RemoteDeviceId, outcome);
            }
        }
    }

    /// <summary>Muestra el código, intercambia las confirmaciones y, si ambos dicen que sí, fija la clave.</summary>
    private async Task<PairingOutcome> ConfirmAsync(PeerConnection conn, PairingPrompt prompt, HelloMessage hello, CancellationToken ct)
    {
        PromptRequested?.Invoke(prompt);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remoteTask = ReadConfirmAsync(conn, cts.Token);
        var localTask = prompt.Decision.WaitAsync(UserTimeout, cts.Token);

        bool local;
        bool remote;
        var first = await Task.WhenAny(remoteTask, localTask).ConfigureAwait(false);
        if (first == remoteTask)
        {
            remote = await SafeResult(remoteTask).ConfigureAwait(false) ?? false;
            if (!remote)
            {
                await TrySendConfirmAsync(conn, false, ct).ConfigureAwait(false);
                return PairingOutcome.RemoteRejected;
            }
            var l = await SafeResult(localTask).ConfigureAwait(false);
            if (l is null)
            {
                await TrySendConfirmAsync(conn, false, ct).ConfigureAwait(false);
                return PairingOutcome.Timeout;
            }
            local = l.Value;
            await TrySendConfirmAsync(conn, local, ct).ConfigureAwait(false);
            if (!local)
                return PairingOutcome.Rejected;
        }
        else
        {
            var l = await SafeResult(localTask).ConfigureAwait(false);
            local = l ?? false;
            await TrySendConfirmAsync(conn, local, ct).ConfigureAwait(false);
            if (!local)
            {
                cts.Cancel();
                return l is null ? PairingOutcome.Timeout : PairingOutcome.Rejected;
            }
            remote = await SafeResult(remoteTask).ConfigureAwait(false) ?? false;
            if (!remote)
                return PairingOutcome.RemoteRejected;
        }

        _trust.AddOrReplace(new TrustedPeer
        {
            DeviceId = conn.RemoteDeviceId,
            PublicKey = Convert.ToBase64String(conn.RemotePublicKey),
            Alias = CleanAlias(hello.Alias),
            Os = hello.Os is { Length: <= 16 } os ? os : null,
            Avatar = Capabilities.AvatarOf(hello.Caps),
            PairedAt = DateTimeOffset.UtcNow,
        });
        CloseWindow();
        return PairingOutcome.Paired;
    }

    private static async Task<bool> ReadConfirmAsync(PeerConnection conn, CancellationToken ct)
    {
        using var frame = await conn.Frames.ReadExpectAsync(UserTimeout + StepTimeout, ct, FrameType.PairConfirm, FrameType.Error).ConfigureAwait(false);
        return frame.Type == FrameType.PairConfirm && frame.Parse(ProtocolJsonContext.Default.PairConfirmMessage).Ok;
    }

    private static async Task<bool?> SafeResult(Task<bool> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task TrySendConfirmAsync(PeerConnection conn, bool ok, CancellationToken ct)
    {
        try
        {
            await conn.Frames.SendAsync(FrameType.PairConfirm, new PairConfirmMessage { Ok = ok }, ProtocolJsonContext.Default.PairConfirmMessage, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void RecordOutcome(PairingOutcome outcome)
    {
        lock (_lock)
        {
            if (outcome == PairingOutcome.Paired)
            {
                _failures = 0;
                return;
            }
            if (outcome is PairingOutcome.Rejected or PairingOutcome.RemoteRejected or PairingOutcome.Tampered)
            {
                _failures++;
                if (_failures >= MaxFailures)
                {
                    _failures = 0;
                    _lockedUntil = DateTimeOffset.UtcNow + LockDuration;
                    _windowUntil = null;
                }
            }
        }
        if (IsLocked)
            WindowChanged?.Invoke();
    }

    private HelloMessage LocalHello() => new()
    {
        V = AppInfo.ProtocolVersion,
        Purpose = Purposes.Pair,
        Alias = _alias(),
        Os = AppInfo.OsName,
        AppVersion = AppInfo.Version,
        Caps = Capabilities.ForHello(_avatar()),
    };

    private static async Task SendErrorAsync(PeerConnection conn, string code, CancellationToken ct)
    {
        try
        {
            await conn.Frames.SendAsync(FrameType.Error, new ErrorMessage { Code = code }, ProtocolJsonContext.Default.ErrorMessage, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static string CleanAlias(string alias) => TextLimits.CleanAlias(alias) ?? "Equipo sin nombre";

    private static byte[] DecodeHex32(string hex)
    {
        if (hex.Length != 64)
            throw new ProtocolException("Valor de emparejamiento inválido.");
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            throw new ProtocolException("Valor de emparejamiento inválido.");
        }
    }

    /// <summary>6 dígitos a partir de SHA-256(huella menor ‖ huella mayor ‖ nA ‖ nB).</summary>
    public static string ComputeCode(byte[] initiatorFingerprint, byte[] responderFingerprint, byte[] nA, byte[] nB)
    {
        var (first, second) = initiatorFingerprint.AsSpan().SequenceCompareTo(responderFingerprint) <= 0
            ? (initiatorFingerprint, responderFingerprint)
            : (responderFingerprint, initiatorFingerprint);
        var input = new byte[first.Length + second.Length + nA.Length + nB.Length];
        first.CopyTo(input, 0);
        second.CopyTo(input, first.Length);
        nA.CopyTo(input, first.Length + second.Length);
        nB.CopyTo(input, first.Length + second.Length + nA.Length);
        var hash = SHA256.HashData(input);
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        var digits = value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        return digits[..3] + " " + digits[3..];
    }
}
