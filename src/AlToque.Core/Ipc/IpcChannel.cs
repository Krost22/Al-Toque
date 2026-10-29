using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlToque.Core.Ipc;

public sealed class IpcRequest
{
    /// <summary>"send", "show", "ping", "pair", "connect", "quit".</summary>
    public required string Command { get; init; }

    public List<string>? Paths { get; init; }

    /// <summary>deviceId o alias del destino.</summary>
    public string? To { get; init; }

    public bool Pick { get; init; }
    public bool Code { get; init; }
    public string? Message { get; init; }
}

public sealed class IpcResponse
{
    public required bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Detail { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 4)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJsonContext : JsonSerializerContext;

/// <summary>
/// Canal local entre el CLI (menú contextual, "Enviar a") y el agente. Usa una tubería con nombre limitada al
/// usuario actual (<see cref="PipeOptions.CurrentUserOnly"/>): en Windows el cliente además comprueba que el
/// servidor pertenece al mismo usuario.
/// </summary>
public static class IpcChannel
{
    private const int MaxMessage = 4 << 20;

    public static string PipeName
    {
        get
        {
            return $"{AppInfo.Id}-{InstanceKey}";
        }
    }

    /// <summary>
    /// Clave de instancia por usuario. Con <c>ALTOQUE_HOME</c> (desarrollo y pruebas) también depende de la carpeta
    /// de datos, para poder correr dos agentes en el mismo equipo.
    /// </summary>
    public static string InstanceKey
    {
        get
        {
            var key = Environment.UserDomainName + "\\" + Environment.UserName;
            if (Environment.GetEnvironmentVariable(AppPaths.HomeOverrideVariable) is { Length: > 0 } home)
                key += "|" + Path.GetFullPath(home);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
        }
    }

    public static async Task<IpcResponse?> SendAsync(IpcRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        await WriteAsync(client, JsonSerializer.SerializeToUtf8Bytes(request, IpcJsonContext.Default.IpcRequest), ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var bytes = await ReadAsync(client, cts.Token).ConfigureAwait(false);
        return bytes is null ? null : JsonSerializer.Deserialize(bytes, IpcJsonContext.Default.IpcResponse);
    }

    internal static NamedPipeServerStream CreateServer() =>
        new(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    internal static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    internal static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 0 or > MaxMessage)
            return null;
        var payload = new byte[length];
        return await ReadExactAsync(stream, payload, ct).ConfigureAwait(false) ? payload : null;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                return false;
            read += n;
        }
        return true;
    }
}

/// <summary>Servidor IPC del agente.</summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly Func<IpcRequest, Task<IpcResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private const int ParallelListeners = 4;
    private Task? _loop;

    public IpcServer(Func<IpcRequest, Task<IpcResponse>> handler) => _handler = handler;

    /// <summary>Varias instancias de la tubería a la vez: una selección grande lanza muchos procesos del CLI juntos.</summary>
    public void Start() => _loop = Task.WhenAll(Enumerable.Range(0, ParallelListeners).Select(_ => Task.Run(() => LoopAsync(_cts.Token))));

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = IpcChannel.CreateServer();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                server?.Dispose();
                try
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            _ = Task.Run(() => HandleAsync(server, ct), ct);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var bytes = await IpcChannel.ReadAsync(server, cts.Token).ConfigureAwait(false);
                if (bytes is null)
                    return;
                IpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize(bytes, IpcJsonContext.Default.IpcRequest);
                }
                catch (JsonException)
                {
                    return;
                }
                if (request is null)
                    return;
                var response = await _handler(request).ConfigureAwait(false);
                await IpcChannel.WriteAsync(server, JsonSerializer.SerializeToUtf8Bytes(response, IpcJsonContext.Default.IpcResponse), cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _cts.Dispose();
    }
}
