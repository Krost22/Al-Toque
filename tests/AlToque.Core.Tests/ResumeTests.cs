using System.Security.Cryptography;
using AlToque.Core.Transfer;
using Xunit;

namespace AlToque.Core.Tests;

/// <summary>Un envío entre compañeros sobrevive a un corte de red: sigue desde donde quedó, sin empezar de cero.</summary>
public class ResumeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    static ResumeTests() => Discovery.LocalNetwork.IncludeLoopback = true;

    private static void MakeQuick(TestAgent sender)
    {
        // Bloques de 1 MiB con una pausa: el envío dura lo suficiente como para cortarlo a mitad.
        sender.Host.Client.ChunkDelayForTesting = TimeSpan.FromMilliseconds(15);
        sender.Host.Client.ReconnectDelays = [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(150), 12)];
    }

    private static PeerTarget Trusted(TestAgent from, TestAgent to) =>
        from.TargetFor(to) with { PinnedKey = to.Host.Identity.PublicKey, ExpectedDeviceId = to.Host.DeviceId };

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task TransferPicksUpWhereItLeftOffAfterTheNetworkDrops()
    {
        await using var laura = TestAgent.Create("Laura");
        await using var andres = TestAgent.Create("Andrés");
        await ChatTests.PairAsync(laura, andres);
        MakeQuick(laura);
        var big = laura.WriteFile("video.mp4", 40 * 1024 * 1024);
        var small = laura.WriteFile("notas.txt", 3000);

        var states = new List<TransferState>();
        TransferInfo? received = null;
        var receiverStates = new List<TransferState>();
        andres.Host.TransferStarted += i =>
        {
            if (i.Direction != TransferDirection.Receive)
                return;
            received = i;
            i.StateChanged += x =>
            {
                lock (receiverStates)
                    receiverStates.Add(x.State);
            };
        };
        var info = laura.Host.BeginSend(Trusted(laura, andres), [big, small]);
        info.StateChanged += i =>
        {
            lock (states)
                states.Add(i.State);
        };

        // A mitad del archivo grande, se cae la red (todas las conexiones de la sesión).
        await WaitUntil(() => info.TransferredBytes > 12 * 1024 * 1024);
        var before = info.TransferredBytes;
        andres.Host.Server.DropConnectionsForTesting();

        await TransferIntegrationTests.WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);
        lock (states)
            Assert.Contains(TransferState.Reconnecting, states);
        await WaitUntil(() => received is { IsFinished: true });
        Assert.Equal(TransferState.Completed, received!.State);
        lock (receiverStates)
            Assert.Contains(TransferState.Reconnecting, receiverStates);

        // Llegó entero, una sola vez y sin temporales.
        var files = Directory.GetFiles(andres.Inbox).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["notas.txt", "video.mp4"], files);
        Assert.Equal(Sha(big), Sha(Path.Combine(andres.Inbox, "video.mp4")));
        Assert.Equal(Sha(small), Sha(Path.Combine(andres.Inbox, "notas.txt")));
        Assert.Empty(Directory.GetFiles(andres.Inbox, "*" + AppInfo.PartExtension));
        Assert.Equal(2, info.FilesDone);
        Assert.True(before > 0);
    }

    [Fact]
    public async Task SeveralDropsInARowAreSurvived()
    {
        await using var laura = TestAgent.Create("Laura");
        await using var andres = TestAgent.Create("Andrés");
        await ChatTests.PairAsync(laura, andres);
        MakeQuick(laura);
        var big = laura.WriteFile("respaldo.zip", 48 * 1024 * 1024);
        var info = laura.Host.BeginSend(Trusted(laura, andres), [big]);

        foreach (var mb in new[] { 8, 20, 34 })
        {
            await WaitUntil(() => info.TransferredBytes > mb * 1024L * 1024 && info.State == TransferState.Transferring);
            andres.Host.Server.DropConnectionsForTesting();
        }
        await TransferIntegrationTests.WaitFinished(info);
        Assert.Equal(TransferState.Completed, info.State);
        Assert.Equal(Sha(big), Sha(Path.Combine(andres.Inbox, "respaldo.zip")));
        Assert.Single(Directory.GetFiles(andres.Inbox));
    }

    [Fact]
    public async Task ReceptionGivesUpIfTheSenderNeverComesBack()
    {
        await using var laura = TestAgent.Create("Laura");
        await using var andres = TestAgent.Create("Andrés");
        await ChatTests.PairAsync(laura, andres);
        MakeQuick(laura);
        andres.Host.Server.ParkTimeout = TimeSpan.FromSeconds(1);
        var big = laura.WriteFile("pesado.bin", 40 * 1024 * 1024);
        TransferInfo? received = null;
        andres.Host.TransferStarted += i =>
        {
            if (i.Direction == TransferDirection.Receive)
                received = i;
        };
        var info = laura.Host.BeginSend(Trusted(laura, andres), [big]);
        await WaitUntil(() => info.TransferredBytes > 8 * 1024 * 1024);

        // Laura apaga la PC justo cuando se corta la red: nunca vuelve.
        laura.Host.CancelTransfer(info);
        andres.Host.Server.DropConnectionsForTesting();
        await WaitUntil(() => received is { State: TransferState.Reconnecting } or { IsFinished: true });
        await WaitUntil(() => received is { IsFinished: true });
        Assert.Equal(TransferState.Failed, received!.State);
        // Lo recibido a medias se descarta.
        await WaitUntil(() => Directory.GetFiles(andres.Inbox).Length == 0);
    }

    [Fact]
    public async Task SendingTheSameThingAgainDoesNotMakeCopies()
    {
        await using var laura = TestAgent.Create("Laura");
        await using var andres = TestAgent.Create("Andrés");
        await ChatTests.PairAsync(laura, andres);
        var informe = laura.WriteFile("informe.pdf", 200_000);
        laura.WriteFile("fotos/a.jpg", 50_000);
        laura.WriteFile("fotos/b.jpg", 60_000);
        var fotos = Path.Combine(laura.Source, "fotos");

        async Task Send(params string[] paths)
        {
            var info = laura.Host.BeginSend(Trusted(laura, andres), paths);
            await TransferIntegrationTests.WaitFinished(info);
            Assert.Equal(TransferState.Completed, info.State);
        }

        await Send(informe, fotos);
        // Un reintento o un segundo envío de lo mismo: llega bien, pero no aparecen "informe (1).pdf" ni "fotos (1)".
        await Send(informe, fotos);
        Assert.Equal(["fotos", "informe.pdf"], Directory.EnumerateFileSystemEntries(andres.Inbox).Select(Path.GetFileName).Order());
        Assert.Equal(["a.jpg", "b.jpg"], Directory.GetFiles(Path.Combine(andres.Inbox, "fotos")).Select(Path.GetFileName).Order());

        // Con el mismo nombre pero otro contenido, sí se guarda aparte.
        File.WriteAllBytes(informe, RandomNumberGenerator.GetBytes(1234));
        await Send(informe);
        Assert.Equal(["fotos", "informe (1).pdf", "informe.pdf"], Directory.EnumerateFileSystemEntries(andres.Inbox).Select(Path.GetFileName).Order());
    }

    private static async Task WaitUntil(Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string what = "")
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > Timeout.TotalMilliseconds)
                throw new TimeoutException("No se cumplió a tiempo: " + what);
            await Task.Delay(5);
        }
    }
}
