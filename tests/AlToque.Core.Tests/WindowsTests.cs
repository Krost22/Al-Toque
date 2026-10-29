using System.Security.Cryptography;
using AlToque.Core.Security;
using AlToque.Core.Transfer;

namespace AlToque.Core.Tests;

/// <summary>Comprobaciones que solo tienen sentido en Windows (en otros sistemas terminan sin hacer nada).</summary>
public class WindowsTests
{
    [Fact]
    public void DpapiRoundTripsAndRejectsGarbage()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var protector = new DpapiSecretProtector();
        var secret = RandomNumberGenerator.GetBytes(64);
        var sealedBytes = protector.Protect(secret);
        Assert.NotEqual(secret, sealedBytes);
        Assert.Equal(secret, protector.Unprotect(sealedBytes));
        sealedBytes[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => OperatingSystem.IsWindows() ? protector.Unprotect(sealedBytes) : []);
    }

    [Fact]
    public void IdentityPersistsWithDpapi()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var dir = new TempDir();
        var paths = AppPaths.Create(dir.Path, false);
        var protector = new DpapiSecretProtector();
        string id;
        using (var first = DeviceIdentity.LoadOrCreate(paths, protector, out _))
            id = first.DeviceId;
        using var second = DeviceIdentity.LoadOrCreate(paths, protector, out var created);
        Assert.False(created);
        Assert.Equal(id, second.DeviceId);
        Assert.True(second.Certificate.HasPrivateKey);
    }

    [Fact]
    public void MarkOfTheWebIsWritten()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "instalador.exe");
        File.WriteAllText(file, "MZ");
        Assert.True(MarkOfTheWeb.IsSupported(dir.Path));
        MarkOfTheWeb.Apply(file, "prueba");
        var zone = File.ReadAllText(file + ":Zone.Identifier");
        Assert.Contains("ZoneId=3", zone);
    }
}
