using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using AlToque.Core;
using AlToque.Core.Agent;
using AlToque.Core.Discovery;
using AlToque.Core.Pairing;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Mobile;

/// <summary>
/// Prueba de punta a punta en el propio dispositivo: dos agentes en local se emparejan (TLS mutuo, código de 6 cifras)
/// y se pasan un archivo que se compara byte a byte. El CI la corre en un emulador de Android.
/// </summary>
public static class SelfTest
{
    public const string Marker = "ALTOQUE_SELFTEST";

    public static async Task<string> RunAsync(string workDirectory)
    {
        var watch = Stopwatch.StartNew();
        var root = Path.Combine(workDirectory, "selftest-" + Guid.NewGuid().ToString("N")[..8]);
        AgentHost? a = null, b = null;
        try
        {
            LocalNetwork.IncludeLoopback = true;
            var interfaces = LocalNetwork.GetIPv4Interfaces(excludeVirtual: false);
            var networkInfo = string.Join(", ", interfaces.Select(i => $"{i.Name}={i.Address}/{i.Mask}"));
            a = CreateAgent(Path.Combine(root, "a"), "Prueba A");
            b = CreateAgent(Path.Combine(root, "b"), "Prueba B");

            // 1. Emparejar: B abre la ventana y los dos confirman el mismo código.
            string? codeA = null, codeB = null;
            a.Pairing.PromptRequested += p =>
            {
                codeA = p.Code;
                p.Confirm(true);
            };
            b.Pairing.PromptRequested += p =>
            {
                codeB = p.Code;
                p.Confirm(true);
            };
            b.Pairing.OpenWindow();
            var outcome = await a.Pairing.PairWithAsync(new IPEndPoint(IPAddress.Loopback, b.Server.Port), b.DeviceId, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(60));
            if (outcome != PairingOutcome.Paired || codeA is null || codeA != codeB)
                return Fail($"emparejamiento: {outcome} ({codeA} / {codeB})");

            // 2. Enviar un archivo de 3 MB entre compañeros de confianza.
            b.OfferReceived += o => o.Accept();
            var received = new TaskCompletionSource<TransferInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            b.TransferFinished += i => received.TrySetResult(i);
            var source = Path.Combine(root, "a-src", "prueba.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            var data = RandomNumberGenerator.GetBytes(3 * 1024 * 1024);
            await File.WriteAllBytesAsync(source, data);
            var target = new PeerTarget { EndPoint = new IPEndPoint(IPAddress.Loopback, b.Server.Port), DisplayName = "Prueba B" };
            var sent = a.BeginSend(target, [source]);
            var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(90));
            while (!sent.IsFinished)
                await Task.Delay(50);
            if (sent.State != TransferState.Completed || got.State != TransferState.Completed)
                return Fail($"transferencia: envío {sent.State} {sent.ErrorCode} {sent.ErrorDetail} / recepción {got.State} {got.ErrorCode} {got.ErrorDetail}");
            if (!got.PeerTrusted || !sent.PeerTrusted)
                return Fail("la transferencia no quedó verificada");
            var copy = got.CompletedPaths.FirstOrDefault();
            if (copy is null || !(await File.ReadAllBytesAsync(copy)).AsSpan().SequenceEqual(data))
                return Fail("el archivo recibido no coincide");
            return $"{Marker} OK en {watch.Elapsed.TotalSeconds:0.0} s · código {codeA} · {AppInfo.OsName} · red: {networkInfo}";
        }
        catch (Exception ex)
        {
            return Fail(ex.ToString());
        }
        finally
        {
            if (a is not null)
                await a.DisposeAsync();
            if (b is not null)
                await b.DisposeAsync();
            OutgoingFiles.Delete(root);
        }
    }

    private static string Fail(string why) => $"{Marker} FAIL {why}";

    private static AgentHost CreateAgent(string dir, string alias)
    {
        var host = AgentHost.Create(new AgentOptions
        {
            Paths = AppPaths.Create(Path.Combine(dir, "config"), isPortable: false),
            EnableIpc = false,
            EnableDiscovery = false,
        });
        host.Settings.Update(s =>
        {
            s.Alias = alias;
            s.TcpPort = Random.Shared.Next(46000, 60000);
            s.ReceiveFolder = Path.Combine(dir, "inbox");
            s.WelcomeDone = true;
        });
        host.Start();
        return host;
    }
}
