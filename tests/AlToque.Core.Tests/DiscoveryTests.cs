using AlToque.Core.Discovery;
using AlToque.Core.Security;

namespace AlToque.Core.Tests;

[Collection("network")]
public class DiscoveryTests
{
    [Fact]
    public async Task TwoInstancesFindEachOther()
    {
        LocalNetwork.IncludeLoopback = true;
        if (LocalNetwork.GetIPv4Interfaces(excludeVirtual: false).Count == 0)
            return;
        var port = Random.Shared.Next(47000, 48000);
        var idA = Base32.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var idB = Base32.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        using var a = new DiscoveryService(idA, () => new DiscoveryAdvert(1111, "Laura", "windows", false, false), () => false, port);
        using var b = new DiscoveryService(idB, () => new DiscoveryAdvert(2222, null, null, false, false), () => false, port);
        var seenByB = new TaskCompletionSource<DiscoveryHint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenByA = new TaskCompletionSource<DiscoveryHint>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.HintReceived += h => { if (h.DeviceId == idA) seenByB.TrySetResult(h); };
        a.HintReceived += h => { if (h.DeviceId == idB) seenByA.TrySetResult(h); };
        a.Start();
        b.Start();
        Assert.True(a.IsRunning && b.IsRunning, a.LastError ?? b.LastError);
        b.Query();
        var hintA = await seenByB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Laura", hintA.Alias);
        Assert.Equal(1111, hintA.EndPoint.Port);
        a.Query();
        var hintB = await seenByA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(hintB.Alias);
        Assert.Equal(2222, hintB.EndPoint.Port);

        var bye = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.ByeReceived += id => bye.TrySetResult(id);
        a.Stop();
        Assert.Equal(idA, await bye.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
