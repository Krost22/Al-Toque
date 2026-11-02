using AlToque.Core.Pairing;
using AlToque.Core.Storage;
using Xunit;

namespace AlToque.Core.Tests;

/// <summary>Conectar en pocos clics: pedir conexión a un equipo cercano sin que abra «Emparejar».</summary>
public class ConnectRequestTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    static ConnectRequestTests() => Discovery.LocalNetwork.IncludeLoopback = true;

    [Fact]
    public async Task NearbyDeviceCanAskToConnectWithoutOpeningPairing()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés", pairRequests: true);
        PairingPrompt? shown = null;
        a.Host.Pairing.PromptRequested += p => p.Confirm(true);
        b.Host.Pairing.PromptRequested += p =>
        {
            shown = p;
            p.Confirm(true);
        };
        Assert.False(b.Host.Pairing.IsWindowOpen);

        var outcome = await a.Host.Pairing.PairWithAsync(b.EndPoint, b.Host.DeviceId, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(PairingOutcome.Paired, outcome);
        Assert.NotNull(shown);
        Assert.True(shown!.IsRequest);
        Assert.Equal("Laura", shown.PeerName);
        await WaitUntil(() => b.Host.Trust.Find(a.Host.DeviceId) is not null);
        Assert.NotNull(a.Host.Trust.Find(b.Host.DeviceId));
    }

    [Fact]
    public async Task RequestsAreRefusedWhenTheDeviceDoesNotTakeThem()
    {
        // Sin interfaz para solicitudes (versión del teléfono, por ahora) o en «Nadie»: hay que abrir «Emparejar».
        await using var a = TestAgent.Create("Laura");
        await using var plain = TestAgent.Create("Teléfono");
        var prompted = false;
        plain.Host.Pairing.PromptRequested += _ => prompted = true;
        Assert.Equal(PairingOutcome.NotPairing,
            await a.Host.Pairing.PairWithAsync(plain.EndPoint, null, CancellationToken.None).WaitAsync(Timeout));

        await using var hidden = TestAgent.Create("Oculto", pairRequests: true);
        hidden.Host.Settings.Update(s => s.ReceiveMode = ReceiveMode.Nobody);
        hidden.Host.Pairing.PromptRequested += _ => prompted = true;
        // En «Nadie» deja de escuchar: ni siquiera se puede conectar.
        var outcome = await a.Host.Pairing.PairWithAsync(hidden.EndPoint, null, CancellationToken.None).WaitAsync(Timeout);
        Assert.NotEqual(PairingOutcome.Paired, outcome);

        await using var invisible = TestAgent.Create("Discreto", pairRequests: true);
        invisible.Host.Settings.Update(s => s.VisibleNearby = false);
        invisible.Host.Pairing.PromptRequested += _ => prompted = true;
        Assert.Equal(PairingOutcome.NotPairing,
            await a.Host.Pairing.PairWithAsync(invisible.EndPoint, null, CancellationToken.None).WaitAsync(Timeout));
        Assert.False(prompted);
    }

    [Fact]
    public async Task AfterNotNowTheSameDeviceMustWait()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés", pairRequests: true);
        a.Host.Pairing.PromptRequested += p => p.Confirm(true);
        var prompts = 0;
        b.Host.Pairing.PromptRequested += p =>
        {
            prompts++;
            p.Confirm(false); // «Ahora no»
        };

        Assert.Equal(PairingOutcome.RemoteRejected,
            await a.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout));
        // Insistir enseguida no vuelve a molestar a Andrés.
        Assert.Equal(PairingOutcome.NotPairing,
            await a.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout));
        Assert.Equal(1, prompts);

        // Con «Emparejar» abierto a propósito, sí puede.
        b.Host.Pairing.OpenWindow();
        Assert.Equal(PairingOutcome.RemoteRejected,
            await a.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout));
        Assert.Equal(2, prompts);
    }

    [Fact]
    public async Task OnlyAFewRequestsPerMinuteAreShown()
    {
        await using var b = TestAgent.Create("Andrés", pairRequests: true);
        var prompts = 0;
        b.Host.Pairing.PromptRequested += p =>
        {
            Interlocked.Increment(ref prompts);
            p.Confirm(false);
        };
        // Cuatro equipos distintos piden seguido: el cuarto ya no se muestra.
        for (var i = 0; i < 4; i++)
        {
            await using var asker = TestAgent.Create("Equipo " + i);
            asker.Host.Pairing.PromptRequested += p => p.Confirm(true);
            await asker.Host.Pairing.PairWithAsync(b.EndPoint, null, CancellationToken.None).WaitAsync(Timeout);
        }
        Assert.Equal(3, prompts);
    }

    [Fact]
    public async Task NearbyVisibilityFollowsModeNetworkAndSetting()
    {
        await using var a = TestAgent.Create("Laura", pairRequests: true);
        Assert.True(a.Host.IsVisibleNearby);
        Assert.True(a.Host.AcceptsPairRequests);

        a.Host.Settings.Update(s => s.VisibleNearby = false);
        Assert.False(a.Host.IsVisibleNearby);
        Assert.False(a.Host.AcceptsPairRequests);

        a.Host.Settings.Update(s =>
        {
            s.VisibleNearby = true;
            s.ReceiveMode = ReceiveMode.Nobody;
        });
        Assert.False(a.Host.IsVisibleNearby);

        await using var cafe = TestAgent.Create("En el café", publicNetwork: true, pairRequests: true);
        cafe.Host.Settings.Update(s => s.AllowPublicNetworks = true);
        Assert.False(cafe.Host.IsVisibleNearby);

        // Sin interfaz para avisos, se ve pero no acepta solicitudes.
        await using var phone = TestAgent.Create("Teléfono");
        Assert.True(phone.Host.IsVisibleNearby);
        Assert.False(phone.Host.AcceptsPairRequests);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > Timeout.TotalMilliseconds)
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(20);
        }
    }
}
