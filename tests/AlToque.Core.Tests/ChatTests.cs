using System.Security.Cryptography;
using AlToque.Core.Chat;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Transfer;
using Xunit;

namespace AlToque.Core.Tests;

public class ChatTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    static ChatTests() => LocalNetwork.IncludeLoopback = true;

    private static string NewDeviceId() => PeerKeys.DeviceIdFromSpki(RandomNumberGenerator.GetBytes(91));

    private static ChatStore OpenStore(TempDir dir, string me) =>
        ChatStore.Load(AppPaths.Create(Path.Combine(dir.Path, "config"), false), new FilePermissionSecretProtector(), me);

    private static ChatWireMessage Wire(long seq, string text) =>
        new() { Id = Guid.NewGuid().ToString("N"), Seq = seq, Time = DateTimeOffset.UtcNow, Text = text };

    // ── Almacén ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ConversationSurvivesRestartAndIsEncryptedOnDisk()
    {
        using var dir = new TempDir();
        var me = NewDeviceId();
        var peer = NewDeviceId();
        var store = OpenStore(dir, me);
        store.AddOwnText(peer, "hola, ¿me pasas el informe?");
        Assert.True(store.AddRemote(peer, Wire(1, "ahí va")));

        var reopened = OpenStore(dir, me);
        var messages = reopened.Messages(peer);
        Assert.Equal(["hola, ¿me pasas el informe?", "ahí va"], messages.Select(m => m.Text));
        Assert.Equal(1, reopened.TheirUpTo(peer));
        // El número de orden sigue donde iba: el siguiente es el 2.
        Assert.Equal(2, reopened.AddOwnText(peer, "gracias").Seq);

        var raw = File.ReadAllText(Path.Combine(dir.Path, "config", "chats", peer + ".log"));
        Assert.DoesNotContain("informe", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteMessagesAreNeverDuplicated()
    {
        using var dir = new TempDir();
        var peer = NewDeviceId();
        var store = OpenStore(dir, NewDeviceId());
        var second = Wire(2, "dos");
        var first = Wire(1, "uno");

        Assert.True(store.AddRemote(peer, second));
        Assert.Equal(0, store.TheirUpTo(peer)); // Falta el 1.
        Assert.True(store.AddRemote(peer, first));
        Assert.Equal(2, store.TheirUpTo(peer));

        // Reenvíos tras una reconexión: mismo id o mismo número, no se repiten.
        Assert.False(store.AddRemote(peer, first));
        Assert.False(store.AddRemote(peer, Wire(2, "dos (otra vez)")));
        Assert.Equal(2, store.Messages(peer).Count);
        Assert.Equal(2, store.Unread(peer));
        store.MarkRead(peer);
        Assert.Equal(0, store.Unread(peer));
    }

    [Fact]
    public void ClearingAConversationKeepsItFromComingBack()
    {
        using var dir = new TempDir();
        var me = NewDeviceId();
        var peer = NewDeviceId();
        var store = OpenStore(dir, me);
        store.AddRemote(peer, Wire(1, "viejo"));
        store.AddOwnText(peer, "mío");
        store.Clear(peer);
        Assert.Empty(store.Messages(peer));

        var reopened = OpenStore(dir, me);
        Assert.Empty(reopened.Messages(peer));
        // Si el compañero vuelve a mandar lo viejo, no reaparece; y mi numeración sigue.
        Assert.False(reopened.AddRemote(peer, Wire(1, "viejo")));
        Assert.Equal(2, reopened.AddOwnText(peer, "nuevo").Seq);
    }

    [Fact]
    public void BrokenLinesAreSkipped()
    {
        using var dir = new TempDir();
        var me = NewDeviceId();
        var peer = NewDeviceId();
        OpenStore(dir, me).AddOwnText(peer, "antes del corte");
        // Un corte de luz a mitad de una línea.
        File.AppendAllText(Path.Combine(dir.Path, "config", "chats", peer + ".log"), "dGVzdA==AAAA\n");
        var reopened = OpenStore(dir, me);
        Assert.Equal("antes del corte", Assert.Single(reopened.Messages(peer)).Text);
    }

    [Fact]
    public void LosingMyLogKeepsNumberingAfterWhatThePeerHas()
    {
        using var dir = new TempDir();
        var me = NewDeviceId();
        var peer = NewDeviceId();
        var store = OpenStore(dir, me);
        // Se borró la carpeta: escribo "hola" con el número 1, pero el compañero ya tiene mis mensajes hasta el 7.
        var hola = store.AddOwnText(peer, "hola");
        Assert.Equal(1, hola.Seq);
        store.CatchUp(peer, 7);
        Assert.Equal([8L], store.OwnAfter(peer, 7).Select(m => m.Seq));
        Assert.Equal(9, store.AddOwnText(peer, "¿sigues?").Seq);

        var reopened = OpenStore(dir, me);
        Assert.Equal([8L, 9L], reopened.OwnAfter(peer, 7).Select(m => m.Seq));
        Assert.All(reopened.Messages(peer), m => Assert.False(m.Delivered));
        // Con la numeración al día no cambia nada.
        reopened.CatchUp(peer, 9);
        Assert.Equal(10, reopened.AddOwnText(peer, "listo").Seq);
    }

    // ── Entre dos equipos ───────────────────────────────────────────────────────

    [Fact]
    public async Task MessagesArriveBothWaysAndGetDelivered()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        await WaitLinked(a, b);

        var sent = a.Host.SendChat(b.Host.DeviceId, "¿Llegó?");
        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Any(m => m.Text == "¿Llegó?"));
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).Single(m => m.Id == sent.Id).Delivered);

        b.Host.SendChat(a.Host.DeviceId, "¡Sí!");
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).Any(m => m.Text == "¡Sí!"));
        Assert.Equal(1, a.Host.Chats.Unread(b.Host.DeviceId));
    }

    [Fact]
    public async Task MessagesWrittenWhileThePeerIsOffArriveWhenItReturns()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        await WaitLinked(a, b);

        // Andrés apaga la PC; Laura escribe igual y también reinicia la suya.
        await b.StopAsync();
        a.Host.SendChat(b.Host.DeviceId, "uno");
        a.Host.SendChat(b.Host.DeviceId, "dos");
        await a.RestartAsync();
        a.Host.SendChat(b.Host.DeviceId, "tres");
        Assert.All(a.Host.Chats.Messages(b.Host.DeviceId), m => Assert.False(m.Delivered));

        await b.RestartAsync();
        Introduce(a, b);
        a.Host.Links.Kick(b.Host.DeviceId);
        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Count == 3);
        Assert.Equal(["uno", "dos", "tres"], b.Host.Chats.Messages(a.Host.DeviceId).Select(m => m.Text));
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).All(m => m.Delivered));

        // Otra reconexión (cambio de red, reinicio) no duplica nada.
        await b.RestartAsync();
        Introduce(a, b);
        a.Host.Links.Kick(b.Host.DeviceId);
        await WaitLinked(a, b);
        await Task.Delay(500);
        Assert.Equal(3, b.Host.Chats.Messages(a.Host.DeviceId).Count);
    }

    [Fact]
    public async Task OnlyOneLinkPerPairEvenIfBothDial()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        a.Host.Links.Kick(b.Host.DeviceId);
        b.Host.Links.Kick(a.Host.DeviceId);
        await WaitLinked(a, b);
        await Task.Delay(1500);
        Assert.Equal(1, a.Host.Links.LinkCount);
        Assert.Equal(1, b.Host.Links.LinkCount);

        a.Host.SendChat(b.Host.DeviceId, "por un solo enlace");
        b.Host.SendChat(a.Host.DeviceId, "de vuelta");
        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Count == 2 && a.Host.Chats.Messages(b.Host.DeviceId).Count == 2);
    }

    [Fact]
    public async Task LinkComesBackAfterThePeerChangesAddress()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        await WaitLinked(a, b);

        // Andrés cambia de red: vuelve con otra dirección (aquí, otro puerto).
        await b.RestartAsync(port: b.Host.Server.Port + 7);
        a.Host.SendChat(b.Host.DeviceId, "¿sigues ahí?");
        await WaitUntil(() => !a.Host.Links.IsLinked(b.Host.DeviceId));
        Introduce(a, b);
        a.Host.Links.Kick(b.Host.DeviceId);
        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Any(m => m.Text == "¿sigues ahí?"));
        Assert.Single(b.Host.Chats.Messages(a.Host.DeviceId));
    }

    [Fact]
    public async Task MessagesStillArriveAfterOneSideLosesItsChats()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        await WaitLinked(a, b);
        b.Host.SendChat(a.Host.DeviceId, "uno");
        b.Host.SendChat(a.Host.DeviceId, "dos");
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).Count == 2);

        // Andrés borra su carpeta de chats y vuelve a escribir: su "tres" sale con el número 1.
        await b.StopAsync();
        Directory.Delete(b.Host.Paths.ChatDir, recursive: true);
        await b.RestartAsync();
        b.Host.SendChat(a.Host.DeviceId, "tres");
        Introduce(a, b);
        Introduce(b, a);
        a.Host.Links.Kick(b.Host.DeviceId);
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).Any(m => m.Text == "tres"));
        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Single().Delivered);

        b.Host.SendChat(a.Host.DeviceId, "cuatro");
        await WaitUntil(() => a.Host.Chats.Messages(b.Host.DeviceId).Count == 4);
        Assert.Equal(["uno", "dos", "tres", "cuatro"], a.Host.Chats.Messages(b.Host.DeviceId).Select(m => m.Text));
    }

    [Fact]
    public async Task FilesExchangedWithAContactAppearInBothChats()
    {
        await using var a = TestAgent.Create("Laura");
        await using var b = TestAgent.Create("Andrés");
        await PairAsync(a, b);
        var target = a.TargetFor(b) with { PinnedKey = b.Host.Identity.PublicKey, ExpectedDeviceId = b.Host.DeviceId };
        var info = a.Host.BeginSend(target, [a.WriteFile("plano.pdf", 4000)], "el plano");
        await TransferIntegrationTests.WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);

        await WaitUntil(() => b.Host.Chats.Messages(a.Host.DeviceId).Any(m => m.IsFiles && m.Result == ChatResults.Ok));
        var sentItem = Assert.Single(a.Host.Chats.Messages(b.Host.DeviceId));
        Assert.True(sentItem.IsFiles);
        Assert.Equal(ChatResults.Ok, sentItem.Result);
        Assert.Equal("plano.pdf", sentItem.Files![0].Name);
        var receivedItem = Assert.Single(b.Host.Chats.Messages(a.Host.DeviceId));
        Assert.Equal("el plano", receivedItem.Text);
        Assert.True(File.Exists(receivedItem.Files![0].Path));
        Assert.Equal(a.Host.DeviceId, receivedItem.Author);
    }

    // ── Ayudas ──────────────────────────────────────────────────────────────────

    internal static async Task PairAsync(TestAgent a, TestAgent b)
    {
        a.Host.Pairing.PromptRequested += p => p.Confirm(true);
        b.Host.Pairing.PromptRequested += p => p.Confirm(true);
        b.Host.Pairing.OpenWindow();
        var outcome = await a.Host.Pairing.PairWithAsync(b.EndPoint, b.Host.DeviceId, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(PairingOutcome.Paired, outcome);
        await WaitUntil(() => b.Host.Trust.Find(a.Host.DeviceId) is not null);
        Introduce(a, b);
        Introduce(b, a);
    }

    /// <summary>Las pruebas no usan descubrimiento: cada uno se entera de dónde está el otro como si llegara su anuncio.</summary>
    internal static void Introduce(TestAgent who, TestAgent other) =>
        who.Host.Peers.OnHint(new DiscoveryHint(other.Host.DeviceId, other.EndPoint, null, "linux", false, false, [Capabilities.Chat]));

    private static Task WaitLinked(TestAgent a, TestAgent b) =>
        WaitUntil(() => a.Host.Links.IsLinked(b.Host.DeviceId) && b.Host.Links.IsLinked(a.Host.DeviceId));

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
