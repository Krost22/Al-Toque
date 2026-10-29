using System.Net;
using System.Security.Cryptography;
using AlToque.Core.Agent;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Core.Tests;

/// <summary>Dos agentes reales hablando por TLS en loopback.</summary>
[Collection("network")]
public class TransferIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    static TransferIntegrationTests() => LocalNetwork.IncludeLoopback = true;

    [Fact]
    public async Task UnknownSenderIsRejectedByTrustedOnlyReceiver()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        var offered = false;
        b.Host.OfferReceived += _ => offered = true;
        var src = a.WriteFile("hola.txt", 10);

        var info = a.Host.BeginSend(a.TargetFor(b), [src]);
        await WaitFinished(info);

        Assert.Equal(TransferState.Failed, info.State);
        Assert.Equal(ErrorCodes.Policy, info.ErrorCode);
        Assert.False(offered);
        Assert.Empty(Directory.EnumerateFileSystemEntries(b.Inbox));
    }

    [Fact]
    public async Task EveryoneModeTransfersFilesAndFoldersIntact()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        IncomingOffer? seen = null;
        b.Host.OfferReceived += o =>
        {
            seen = o;
            o.Accept();
        };
        var receivedTcs = new TaskCompletionSource<TransferInfo>();
        b.Host.TransferFinished += i => receivedTcs.TrySetResult(i);

        a.WriteFile("Proyecto/notas.txt", 4096);
        a.WriteFile("Proyecto/sub/datos.bin", 3_500_000);
        Directory.CreateDirectory(Path.Combine(a.Source, "Proyecto", "vacía"));
        var solo = a.WriteFile("solo.txt", 0);
        File.SetLastWriteTimeUtc(solo, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        var info = a.Host.BeginSend(a.TargetFor(b), [Path.Combine(a.Source, "Proyecto"), solo], "Aquí va el render final");
        await WaitFinished(info);
        var received = await receivedTcs.Task.WaitAsync(Timeout);

        Assert.Equal(TransferState.Completed, info.State);
        Assert.False(info.PeerTrusted);
        Assert.Equal("Andrés", info.PeerName);
        Assert.Equal(TransferState.Completed, received.State);
        Assert.Equal("Laura", received.PeerName);
        Assert.Equal("Aquí va el render final", seen!.Message);
        Assert.False(seen.Trusted);

        AssertSameFile(Path.Combine(a.Source, "Proyecto", "notas.txt"), Path.Combine(b.Inbox, "Proyecto", "notas.txt"));
        AssertSameFile(Path.Combine(a.Source, "Proyecto", "sub", "datos.bin"), Path.Combine(b.Inbox, "Proyecto", "sub", "datos.bin"));
        AssertSameFile(solo, Path.Combine(b.Inbox, "solo.txt"));
        Assert.True(Directory.Exists(Path.Combine(b.Inbox, "Proyecto", "vacía")));
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(b.Inbox, "solo.txt")));
        Assert.Empty(Directory.EnumerateFiles(b.Inbox, "*" + AppInfo.PartExtension, SearchOption.AllDirectories));
        if (OperatingSystem.IsWindows())
            Assert.Contains("ZoneId=3", File.ReadAllText(Path.Combine(b.Inbox, "solo.txt") + ":Zone.Identifier"));

        // Un segundo envío igual no pisa nada: se renombra el primer nivel.
        var again = a.Host.BeginSend(a.TargetFor(b), [solo]);
        await WaitFinished(again);
        Assert.Equal(TransferState.Completed, again.State);
        Assert.True(File.Exists(Path.Combine(b.Inbox, "solo (1).txt")));

        Assert.Contains(a.Host.History.Recent(), h => h.Direction == "sent" && h.Result == "ok");
        Assert.Contains(b.Host.History.Recent(), h => h.Direction == "received" && h.Result == "ok");
    }

    [Fact]
    public async Task ReceiverCanDecline()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Decline();
        var info = a.Host.BeginSend(a.TargetFor(b), [a.WriteFile("x.txt", 10)]);
        await WaitFinished(info);
        Assert.Equal(TransferState.Declined, info.State);
        Assert.Equal(ErrorCodes.Declined, info.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(b.Inbox));
    }

    [Fact]
    public async Task PairingThenTrustedTransfer()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        string? codeA = null, codeB = null;
        a.Host.Pairing.PromptRequested += p =>
        {
            codeA = p.Code;
            p.Confirm(true);
        };
        b.Host.Pairing.PromptRequested += p =>
        {
            codeB = p.Code;
            p.Confirm(true);
        };

        b.Host.Pairing.OpenWindow();
        var outcome = await a.Host.Pairing.PairWithAsync(b.EndPoint, b.Host.DeviceId, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(PairingOutcome.Paired, outcome);
        Assert.NotNull(codeA);
        Assert.Equal(codeA, codeB);
        await WaitUntil(() => b.Host.Trust.Find(a.Host.DeviceId) is not null);
        Assert.Equal("Andrés", a.Host.Trust.Find(b.Host.DeviceId)!.Alias);
        Assert.Equal("Laura", b.Host.Trust.Find(a.Host.DeviceId)!.Alias);
        Assert.False(b.Host.Pairing.IsWindowOpen);

        // Ya en "Solo confianza", con la clave fijada.
        b.Host.OfferReceived += o =>
        {
            Assert.True(o.Trusted);
            o.Accept();
        };
        var target = a.TargetFor(b) with { PinnedKey = b.Host.Identity.PublicKey, ExpectedDeviceId = b.Host.DeviceId };
        var info = a.Host.BeginSend(target, [a.WriteFile("render.mp4", 1_200_000)]);
        await WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);
        Assert.True(info.PeerTrusted);

        // Con aceptación automática ni siquiera se pregunta.
        b.Host.Trust.Update(a.Host.DeviceId, p => p.AutoAccept = true);
        var asked = 0;
        b.Host.OfferReceived += _ => asked++;
        var auto = a.Host.BeginSend(target, [a.WriteFile("auto.txt", 100)]);
        await WaitFinished(auto);
        Assert.Equal(TransferState.Completed, auto.State);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task PairingFailsWhenCodesAreRejected()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        a.Host.Pairing.PromptRequested += p => p.Confirm(true);
        b.Host.Pairing.PromptRequested += p => p.Confirm(false);
        b.Host.Pairing.OpenWindow();
        var outcome = await a.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(PairingOutcome.RemoteRejected, outcome);
        await Task.Delay(200);
        Assert.Empty(a.Host.Trust.All);
        Assert.Empty(b.Host.Trust.All);
    }

    [Fact]
    public async Task PairingRequiresOpenWindow()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        var outcome = await a.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(PairingOutcome.NotPairing, outcome);
    }

    [Fact]
    public async Task DetectsImpersonationByKey()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        using var real = DeviceIdentity.CreateNew();

        // "PC de Andrés" debería tener otra clave: no se envía nada.
        var pinned = a.TargetFor(b) with { PinnedKey = real.PublicKey };
        var info = a.Host.BeginSend(pinned, [a.WriteFile("secreto.txt", 10)]);
        await WaitFinished(info);
        Assert.Equal(ErrorCodes.IdentityMismatch, info.ErrorCode);

        var expected = a.TargetFor(b) with { ExpectedDeviceId = real.DeviceId };
        var info2 = a.Host.BeginSend(expected, [a.WriteFile("secreto2.txt", 10)]);
        await WaitFinished(info2);
        Assert.Equal(ErrorCodes.IdentityMismatch, info2.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(b.Inbox));
    }

    [Fact]
    public async Task PublicNetworkPausesReceivingUntilAllowed()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés", publicNetwork: true);
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        await WaitUntil(() => b.Host.IsReceivingPausedByNetwork && !b.Host.IsListening);

        // En red pública no recibe: el emisor no puede conectar.
        var port = b.Host.Settings.Current.TcpPort;
        var target = new PeerTarget { EndPoint = new IPEndPoint(IPAddress.Loopback, port), DisplayName = "Andrés" };
        var blocked = a.Host.BeginSend(target, [a.WriteFile("x.txt", 10)]);
        await WaitFinished(blocked);
        Assert.Equal(ErrorCodes.Unreachable, blocked.ErrorCode);

        // "Recibir igual en esta red".
        b.Host.AllowPublicNetworkForSession();
        Assert.False(b.Host.IsReceivingPausedByNetwork);
        Assert.True(b.Host.IsListening);
        var info = a.Host.BeginSend(a.TargetFor(b), [a.WriteFile("y.txt", 10)]);
        await WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);
    }

    [Fact]
    public async Task StaleCachedAddressIsReportedAsUnreachable()
    {
        await using var a = TestAgent.Create("Laura");
        await using var c = TestAgent.Create("Otro equipo");
        c.Host.SetReceiveMode(ReceiveMode.Everyone);
        using var andres = DeviceIdentity.CreateNew();
        // La IP que la caché recordaba para Andrés ahora la tiene otro equipo con Al Toque.
        var stale = a.TargetFor(c) with { PinnedKey = andres.PublicKey, ExpectedDeviceId = andres.DeviceId, FromCache = true };
        var info = a.Host.BeginSend(stale, [a.WriteFile("x.txt", 10)]);
        await WaitFinished(info);
        Assert.Equal(ErrorCodes.Unreachable, info.ErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(c.Inbox));
    }

    [Fact]
    public async Task ConnectionCodeVerifiesFingerprint()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();

        var good = ConnectionCode.Create(IPAddress.Loopback, b.Host.Server.Port, b.Host.Identity.Fingerprint);
        var info = a.Host.BeginSend(AgentHost.TargetForCode(good)!, [a.WriteFile("c.txt", 10)]);
        await WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);

        var wrongFp = (byte[])b.Host.Identity.Fingerprint.Clone();
        wrongFp[0] ^= 0xFF;
        var bad = ConnectionCode.Create(IPAddress.Loopback, b.Host.Server.Port, wrongFp);
        var info2 = a.Host.BeginSend(AgentHost.TargetForCode(bad)!, [a.WriteFile("d.txt", 10)]);
        await WaitFinished(info2);
        Assert.Equal(ErrorCodes.IdentityMismatch, info2.ErrorCode);
    }

    [Fact]
    public async Task BurstOfManySmallFilesPlusLargeOne()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        for (int i = 0; i < 600; i++)
            a.WriteFile($"Build/assets/f{i:D4}.dat", 1000 + i);
        a.WriteFile("Build/juego.apk", 12_000_000);

        var info = a.Host.BeginSend(a.TargetFor(b), [Path.Combine(a.Source, "Build")]);
        await WaitFinished(info);

        Assert.Equal(TransferState.Completed, info.State);
        Assert.Equal(601, info.FilesDone);
        Assert.Equal(600, Directory.GetFiles(Path.Combine(b.Inbox, "Build", "assets")).Length);
        AssertSameFile(Path.Combine(a.Source, "Build", "juego.apk"), Path.Combine(b.Inbox, "Build", "juego.apk"));
        AssertSameFile(Path.Combine(a.Source, "Build", "assets", "f0599.dat"), Path.Combine(b.Inbox, "Build", "assets", "f0599.dat"));
    }

    [Fact]
    public async Task UnreachablePeerReportsCleanly()
    {
        await using var a = TestAgent.Create("Laura");
        var target = new PeerTarget { EndPoint = new IPEndPoint(IPAddress.Loopback, FreePort()), DisplayName = "Nadie" };
        var info = a.Host.BeginSend(target, [a.WriteFile("x.txt", 1)]);
        await WaitFinished(info);
        Assert.Equal(ErrorCodes.Unreachable, info.ErrorCode);
    }

    [Fact]
    public async Task SenderAndReceiverCanCancelMidTransfer()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        TransferInfo? incoming = null;
        b.Host.TransferStarted += i => incoming = i;
        var big = a.WriteSparse("grande.iso", 1L << 30);

        // El receptor cancela.
        var info = a.Host.BeginSend(a.TargetFor(b), [big]);
        await WaitUntil(() => incoming is { TransferredBytes: > 0 });
        b.Host.CancelTransfer(incoming!);
        await WaitFinished(info);
        Assert.Equal(TransferState.Cancelled, info.State);
        await WaitUntil(() => !Directory.EnumerateFiles(b.Inbox, "*", SearchOption.AllDirectories).Any());

        // El emisor cancela.
        incoming = null;
        var info2 = a.Host.BeginSend(a.TargetFor(b), [big]);
        await WaitUntil(() => incoming is { TransferredBytes: > 0 });
        a.Host.CancelTransfer(info2);
        await WaitFinished(info2);
        Assert.Equal(TransferState.Cancelled, info2.State);
        await WaitFinished(incoming!);
        Assert.Equal(TransferState.Cancelled, incoming!.State);
        await WaitUntil(() => !Directory.EnumerateFiles(b.Inbox, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task MaliciousOfferIsRejectedBeforeAskingTheUser()
    {
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        var asked = false;
        b.Host.OfferReceived += _ => asked = true;
        using var attacker = DeviceIdentity.CreateNew();

        await using var conn = await RawHelloAsync(attacker, b);
        await conn.Frames.SendAsync(FrameType.Offer, new OfferMessage
        {
            SessionId = Base32.Encode(RandomNumberGenerator.GetBytes(16)),
            Files = [new OfferFile { FileId = "1", Path = "../../Inicio/malo.exe", Size = 4 }],
            TotalSize = 4,
        }, ProtocolJsonContext.Default.OfferMessage, default);
        using var decision = await conn.Frames.ReadExpectAsync(TimeSpan.FromSeconds(10), default, FrameType.Decision);
        var d = decision.Parse(ProtocolJsonContext.Default.DecisionMessage);
        Assert.Equal(DecisionResults.Declined, d.Result);
        Assert.Equal(ErrorCodes.BadManifest, d.Reason);
        Assert.False(asked);
    }

    [Fact]
    public async Task DataWithoutValidTokenIsNeverWritten()
    {
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        TransferInfo? finished = null;
        b.Host.TransferFinished += i => finished = i;
        using var sender = DeviceIdentity.CreateNew();
        using var thief = DeviceIdentity.CreateNew();
        var sessionId = Base32.Encode(RandomNumberGenerator.GetBytes(16));

        await using var control = await RawHelloAsync(sender, b);
        await control.Frames.SendAsync(FrameType.Offer, new OfferMessage
        {
            SessionId = sessionId,
            Files = [new OfferFile { FileId = "1", Path = "a.txt", Size = 4 }],
            TotalSize = 4,
        }, ProtocolJsonContext.Default.OfferMessage, default);
        using var decisionFrame = await control.Frames.ReadExpectAsync(TimeSpan.FromSeconds(10), default, FrameType.Decision);
        var decision = decisionFrame.Parse(ProtocolJsonContext.Default.DecisionMessage);
        Assert.Equal(DecisionResults.Accepted, decision.Result);
        var token = decision.Files!.Single().Token;

        // Otra clave con el token robado: se descarta sin escribir nada.
        await using (var stolen = await PeerConnection.ConnectAsync(b.EndPoint, thief, _ => true, default))
        {
            await stolen.Frames.SendAsync(FrameType.StreamOpen, new StreamOpenMessage { SessionId = sessionId, FileId = "1", Token = token },
                ProtocolJsonContext.Default.StreamOpenMessage, default);
            await SendChunk(stolen, "MALO"u8.ToArray());
            // El receptor corta la conexión sin responder (cierre limpio o reinicio, según el sistema).
            try
            {
                Assert.Null(await stolen.Frames.ReadOrEndAsync(TimeSpan.FromSeconds(5), default));
            }
            catch (IOException)
            {
            }
        }

        // El emisor legítimo con un token falso: la sesión se cierra.
        await using (var data = await PeerConnection.ConnectAsync(b.EndPoint, sender, _ => true, default))
        {
            await data.Frames.SendAsync(FrameType.StreamOpen, new StreamOpenMessage { SessionId = sessionId, FileId = "1", Token = Base32.Encode(new byte[16]) },
                ProtocolJsonContext.Default.StreamOpenMessage, default);
            await WaitUntil(() => finished is not null);
        }
        Assert.Equal(TransferState.Failed, finished!.State);
        Assert.Equal(ErrorCodes.Protocol, finished.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(b.Inbox, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExtraBytesAbortTheSession()
    {
        await using var b = TestAgent.Create("Andrés");
        b.Host.SetReceiveMode(ReceiveMode.Everyone);
        b.Host.OfferReceived += o => o.Accept();
        TransferInfo? finished = null;
        b.Host.TransferFinished += i => finished = i;
        using var sender = DeviceIdentity.CreateNew();
        var sessionId = Base32.Encode(RandomNumberGenerator.GetBytes(16));

        await using var control = await RawHelloAsync(sender, b);
        await control.Frames.SendAsync(FrameType.Offer, new OfferMessage
        {
            SessionId = sessionId,
            Files = [new OfferFile { FileId = "1", Path = "a.txt", Size = 4 }],
            TotalSize = 4,
        }, ProtocolJsonContext.Default.OfferMessage, default);
        using var decisionFrame = await control.Frames.ReadExpectAsync(TimeSpan.FromSeconds(10), default, FrameType.Decision);
        var token = decisionFrame.Parse(ProtocolJsonContext.Default.DecisionMessage).Files!.Single().Token;

        await using var data = await PeerConnection.ConnectAsync(b.EndPoint, sender, _ => true, default);
        await data.Frames.SendAsync(FrameType.StreamOpen, new StreamOpenMessage { SessionId = sessionId, FileId = "1", Token = token },
            ProtocolJsonContext.Default.StreamOpenMessage, default);
        await SendChunk(data, "12345678"u8.ToArray());
        await WaitUntil(() => finished is not null);
        Assert.Equal(TransferState.Failed, finished!.State);
        Assert.Empty(Directory.EnumerateFiles(b.Inbox, "*", SearchOption.AllDirectories));
    }

    // ── Utilidades ───────────────────────────────────────────────────────────────

    private static async Task<PeerConnection> RawHelloAsync(DeviceIdentity identity, TestAgent target)
    {
        var conn = await PeerConnection.ConnectAsync(target.EndPoint, identity, _ => true, default);
        await conn.Frames.SendAsync(FrameType.Hello, new HelloMessage
        {
            V = 1, Purpose = Purposes.Transfer, Alias = "Atacante", Os = "linux", AppVersion = "0", Caps = [],
        }, ProtocolJsonContext.Default.HelloMessage, default);
        using var hello = await conn.Frames.ReadExpectAsync(TimeSpan.FromSeconds(10), default, FrameType.Hello);
        return conn;
    }

    private static Task SendChunk(PeerConnection conn, byte[] data)
    {
        var buffer = new byte[FrameChannel.HeaderSize + data.Length];
        data.CopyTo(buffer, FrameChannel.HeaderSize);
        return conn.Frames.SendChunkAsync(buffer, data.Length, default);
    }

    internal static async Task WaitFinished(TransferInfo info)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        info.StateChanged += i =>
        {
            if (i.IsFinished)
                tcs.TrySetResult();
        };
        if (!info.IsFinished)
            await tcs.Task.WaitAsync(Timeout);
        await Task.Delay(50);
    }

    internal static async Task WaitUntil(Func<bool> condition)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > Timeout.TotalMilliseconds)
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(10);
        }
    }

    private static void AssertSameFile(string expected, string actual)
    {
        Assert.True(File.Exists(actual), "Falta " + actual);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(expected)), SHA256.HashData(File.ReadAllBytes(actual)));
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

[CollectionDefinition("network", DisableParallelization = true)]
public sealed class NetworkCollection;

internal sealed class TestAgent : IAsyncDisposable
{
    private readonly TempDir _dir;

    private TestAgent(TempDir dir, AgentHost host)
    {
        _dir = dir;
        Host = host;
    }

    public AgentHost Host { get; }
    public string Source => Path.Combine(_dir.Path, "src");
    public string Inbox => Path.Combine(_dir.Path, "inbox");
    public IPEndPoint EndPoint => new(IPAddress.Loopback, Host.Server.Port);

    public static TestAgent Create(string alias, bool publicNetwork = false)
    {
        LocalNetwork.IncludeLoopback = true;
        var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "src"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "inbox"));
        var host = AgentHost.Create(new AgentOptions
        {
            Paths = AppPaths.Create(Path.Combine(dir.Path, "config"), false),
            Protector = new FilePermissionSecretProtector(),
            EnableIpc = false,
            EnableDiscovery = false,
            IsPublicNetwork = () => publicNetwork,
        });
        host.Settings.Update(s =>
        {
            s.Alias = alias;
            s.TcpPort = Random.Shared.Next(46000, 60000);
            s.ReceiveFolder = Path.Combine(dir.Path, "inbox");
        });
        host.Start();
        return new TestAgent(dir, host);
    }

    public PeerTarget TargetFor(TestAgent other) => new() { EndPoint = other.EndPoint, DisplayName = "destino" };

    public string WriteFile(string relative, int size)
    {
        var path = Path.Combine(Source, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    public string WriteSparse(string relative, long size)
    {
        var path = Path.Combine(Source, relative);
        using var fs = File.Create(path);
        fs.SetLength(size);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        _dir.Dispose();
    }
}
