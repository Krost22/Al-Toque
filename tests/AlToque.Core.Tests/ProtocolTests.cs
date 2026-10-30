using System.Buffers.Binary;
using System.Net;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public async Task FramesRoundTrip()
    {
        var ms = new MemoryStream();
        var channel = new FrameChannel(ms);
        await channel.SendAsync(FrameType.Error, new ErrorMessage { Code = "E_TEST", Message = "hola" }, ProtocolJsonContext.Default.ErrorMessage, default);
        var buffer = new byte[FrameChannel.HeaderSize + 3];
        buffer[5] = 1; buffer[6] = 2; buffer[7] = 3;
        await channel.SendChunkAsync(buffer, 3, default);

        ms.Position = 0;
        var reader = new FrameChannel(ms);
        using (var f1 = await reader.ReadAsync(TimeSpan.FromSeconds(1), default))
        {
            Assert.Equal(FrameType.Error, f1.Type);
            Assert.Equal("E_TEST", f1.Parse(ProtocolJsonContext.Default.ErrorMessage).Code);
        }
        using (var f2 = await reader.ReadAsync(TimeSpan.FromSeconds(1), default))
        {
            Assert.Equal(FrameType.Chunk, f2.Type);
            Assert.Equal(new byte[] { 1, 2, 3 }, f2.Payload.ToArray());
        }
        Assert.Null(await reader.ReadOrEndAsync(TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task RejectsUnknownTypeAndOversizedFrames()
    {
        var unknown = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(unknown, 0);
        unknown[4] = 99;
        await Assert.ThrowsAsync<ProtocolException>(() => new FrameChannel(new MemoryStream(unknown)).ReadAsync(TimeSpan.FromSeconds(1), default));

        var huge = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(huge, FrameChannel.MaxControlPayload + 1);
        huge[4] = (byte)FrameType.Hello;
        await Assert.ThrowsAsync<ProtocolException>(() => new FrameChannel(new MemoryStream(huge)).ReadAsync(TimeSpan.FromSeconds(1), default));

        var truncated = new byte[7];
        BinaryPrimitives.WriteUInt32BigEndian(truncated, 10);
        truncated[4] = (byte)FrameType.Chunk;
        await Assert.ThrowsAnyAsync<IOException>(() => new FrameChannel(new MemoryStream(truncated)).ReadAsync(TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task StrictJsonRejectsUnknownFieldsAndMissingRequired()
    {
        static async Task<Frame> FrameOf(string json)
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            var bytes = new byte[5 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)payload.Length);
            bytes[4] = (byte)FrameType.FileResult;
            payload.CopyTo(bytes, 5);
            return await new FrameChannel(new MemoryStream(bytes)).ReadAsync(TimeSpan.FromSeconds(1), default);
        }
        using (var ok = await FrameOf("""{"fileId":"1","ok":true}"""))
            Assert.True(ok.Parse(ProtocolJsonContext.Default.FileResultMessage).Ok);
        using (var extra = await FrameOf("""{"fileId":"1","ok":true,"admin":true}"""))
            Assert.Throws<ProtocolException>(() => extra.Parse(ProtocolJsonContext.Default.FileResultMessage));
        using (var missing = await FrameOf("""{"ok":true}"""))
            Assert.Throws<ProtocolException>(() => missing.Parse(ProtocolJsonContext.Default.FileResultMessage));
        using (var nul = await FrameOf("""{"fileId":null,"ok":true}"""))
            Assert.Throws<ProtocolException>(() => nul.Parse(ProtocolJsonContext.Default.FileResultMessage));
    }

    [Fact]
    public void Base32MatchesRfc4648()
    {
        Assert.Equal("my", Base32.Encode("f"u8));
        Assert.Equal("mzxw6ytboi", Base32.Encode("foobar"u8));
    }

    [Fact]
    public void ConnectionCodeRoundTripsDefaultPort()
    {
        var fp = new byte[32];
        fp[0] = 0xAB; fp[1] = 0xCD;
        var code = ConnectionCode.Create(IPAddress.Parse("192.168.1.42"), AppInfo.DefaultTcpPort, fp);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}$", code);
        var parsed = ConnectionCode.TryParse(code.ToLowerInvariant().Replace("-", " "));
        Assert.NotNull(parsed);
        Assert.Equal(IPAddress.Parse("192.168.1.42"), parsed.Address);
        Assert.Equal(AppInfo.DefaultTcpPort, parsed.Port);
        Assert.True(parsed.MatchesFingerprint(fp));
        fp[0] = 0x00;
        Assert.False(parsed.MatchesFingerprint(fp));
    }

    [Fact]
    public void ConnectionCodeCarriesCustomPortAndToleratesConfusables()
    {
        var fp = new byte[32];
        fp[0] = 0x12; fp[1] = 0x34;
        var code = ConnectionCode.Create(IPAddress.Parse("10.0.0.7"), 50001, fp);
        Assert.Equal(14, code.Length);
        var parsed = ConnectionCode.TryParse(code.Replace('0', 'O').Replace('1', 'l'));
        Assert.NotNull(parsed);
        Assert.Equal(50001, parsed.Port);
        Assert.Equal(IPAddress.Parse("10.0.0.7"), parsed.Address);
        Assert.True(parsed.MatchesFingerprint(fp));
        Assert.Null(ConnectionCode.TryParse("ABC"));
        Assert.Null(ConnectionCode.TryParse("ABCD-EFGU"));
    }

    [Fact]
    public void PairingCodeIsSymmetricAndSixDigits()
    {
        var fpA = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var fpB = Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray();
        var nA = new byte[32];
        var nB = new byte[32];
        nA[0] = 7;
        nB[0] = 9;
        // Cada lado pasa (huella del iniciador, huella del respondedor): el orden canónico lo iguala.
        var code1 = PairingService.ComputeCode(fpA, fpB, nA, nB);
        var code2 = PairingService.ComputeCode(fpA, fpB, nA, nB);
        Assert.Equal(code1, code2);
        Assert.Matches("^[0-9]{3} [0-9]{3}$", code1);
        // Una huella distinta (intermediario) cambia el código.
        var fpM = (byte[])fpB.Clone();
        fpM[5] ^= 1;
        Assert.NotEqual(code1, PairingService.ComputeCode(fpA, fpM, nA, nB));
        // Intercambiar los nonces también lo cambia: los papeles importan.
        Assert.NotEqual(code1, PairingService.ComputeCode(fpA, fpB, nB, nA));
    }

    [Fact]
    public void DiscoveryPacketParsingIsStrict()
    {
        var id = Base32.Encode(new byte[16]);
        var good = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = id, Port = 45831, N = "abc" };
        Assert.NotNull(DiscoveryPacket.TryParse(good.ToBytes()));
        var bad = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = "no-es-id", Port = 45831, N = "abc" };
        Assert.Null(DiscoveryPacket.TryParse(bad.ToBytes()));
        var noPort = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = id, Port = 0, N = "abc" };
        Assert.Null(DiscoveryPacket.TryParse(noPort.ToBytes()));
        var bidiAlias = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = id, Port = 1, N = "abc", Alias = "PC‮de Andrés" };
        Assert.Null(DiscoveryPacket.TryParse(bidiAlias.ToBytes()));
        var other = new DiscoveryPacket { App = "localsend", V = 1, T = "announce", Id = id, Port = 1, N = "abc" };
        Assert.Null(DiscoveryPacket.TryParse(other.ToBytes()));
        Assert.Null(DiscoveryPacket.TryParse(new byte[2000]));
        Assert.Null(DiscoveryPacket.TryParse("{"u8));
    }

    [Fact]
    public void IdentityIsStableAndDerivedFromKey()
    {
        using var a = DeviceIdentity.CreateNew();
        using var b = DeviceIdentity.CreateNew();
        Assert.True(PeerKeys.IsValidDeviceId(a.DeviceId));
        Assert.NotEqual(a.DeviceId, b.DeviceId);
        Assert.Equal(a.DeviceId, PeerKeys.DeviceIdFromSpki(a.PublicKey));
        Assert.True(a.Certificate.HasPrivateKey);
        Assert.DoesNotContain(Environment.UserName, a.Certificate.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^#[0-9A-F]{6}$", IdentityVisuals.ColorHex(a.DeviceId));
        Assert.Equal(IdentityVisuals.ColorHex(a.DeviceId), IdentityVisuals.ColorHex(a.DeviceId));
    }

    [Fact]
    public void IdentityPersistsAcrossLoads()
    {
        using var dir = new TempDir();
        var paths = AppPaths.Create(dir.Path, false);
        var protector = new FilePermissionSecretProtector();
        string id;
        using (var first = DeviceIdentity.LoadOrCreate(paths, protector, out var created))
        {
            Assert.True(created);
            id = first.DeviceId;
        }
        using var second = DeviceIdentity.LoadOrCreate(paths, protector, out var created2);
        Assert.False(created2);
        Assert.Equal(id, second.DeviceId);
    }

    [Fact]
    public void SealerDetectsTampering()
    {
        var sealer = Sealer.ForTests(new byte[32]);
        var sealedText = sealer.Seal("""{"alias":"Laura"}""");
        Assert.Equal("""{"alias":"Laura"}""", sealer.Unseal(sealedText));
        var tampered = sealedText.Replace("Laura", "Mallory", StringComparison.Ordinal);
        Assert.Null(sealer.Unseal(tampered));
        Assert.Null(Sealer.ForTests(new byte[32].Select(_ => (byte)1).ToArray()).Unseal(sealedText));
        Assert.Null(sealer.Unseal("no es json"));
    }

    [Fact]
    public void TrustStorePinsExactKeyAndDiscardsTamperedFile()
    {
        using var dir = new TempDir();
        var paths = AppPaths.Create(dir.Path, false);
        var protector = new FilePermissionSecretProtector();
        var sealer = Sealer.LoadOrCreate(paths, protector);
        using var peer = DeviceIdentity.CreateNew();
        using var impostor = DeviceIdentity.CreateNew();

        var store = TrustStore.Load(paths, sealer);
        store.AddOrReplace(new TrustedPeer
        {
            DeviceId = peer.DeviceId,
            PublicKey = Convert.ToBase64String(peer.PublicKey),
            Alias = "Andrés",
            PairedAt = DateTimeOffset.UtcNow,
        });
        Assert.True(store.IsTrustedKey(peer.PublicKey));
        Assert.False(store.IsTrustedKey(impostor.PublicKey));

        var reloaded = TrustStore.Load(paths, Sealer.LoadOrCreate(paths, protector));
        Assert.False(reloaded.WasTampered);
        Assert.Single(reloaded.All);

        // Alguien edita el archivo a mano para añadir su clave.
        var text = File.ReadAllText(paths.TrustFile).Replace("Andr", "Mall", StringComparison.Ordinal);
        File.WriteAllText(paths.TrustFile, text);
        var afterTamper = TrustStore.Load(paths, Sealer.LoadOrCreate(paths, protector));
        Assert.True(afterTamper.WasTampered);
        Assert.Empty(afterTamper.All);
    }

    [Fact]
    public void AliasCleaningRemovesControlAndBidi()
    {
        Assert.Equal("PC de Andrés", TextLimits.CleanAlias("  PC de‮ Andrés \n"));
        Assert.Null(TextLimits.CleanAlias("​‮"));
        Assert.Equal(40, TextLimits.CleanAlias(new string('x', 100))!.Length);
    }

    [Fact]
    public void AvatarTravelsInsideHelloCapabilitiesAndIsValidated()
    {
        var caps = Capabilities.ForHello("gato");
        Assert.Contains(Capabilities.Burst, caps);
        Assert.Equal("gato", Capabilities.AvatarOf(caps));
        Assert.Equal([.. Capabilities.Local], Capabilities.ForHello(null));
        Assert.Equal([.. Capabilities.Local], Capabilities.ForHello("../../x"));
        Assert.Null(Capabilities.AvatarOf(["av:GATO"]));
        Assert.Null(Capabilities.AvatarOf(["av:" + new string('a', 40)]));
        Assert.Null(Capabilities.AvatarOf(null));
        Assert.Null(TextLimits.CleanAvatar("a"));
        Assert.Equal("pinguino", TextLimits.CleanAvatar("pinguino"));
    }

    [Fact]
    public void DiscoveryPacketCarriesAnOptionalAvatar()
    {
        var id = DeviceIdentity.CreateNew().DeviceId;
        var good = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = id, Port = 45831, N = "abc", Alias = "Laura", Av = "rana" };
        Assert.Equal("rana", DiscoveryPacket.TryParse(good.ToBytes())!.Av);
        var bad = new DiscoveryPacket { App = "altoque", V = 1, T = "announce", Id = id, Port = 45831, N = "abc", Av = "Rana!" };
        Assert.Null(DiscoveryPacket.TryParse(bad.ToBytes()));
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "altoque-test-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
