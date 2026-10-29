using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AlToque.Core.Protocol;

/// <summary>Trama recibida. El contenido vive en un búfer alquilado que se devuelve al liberar.</summary>
public sealed class Frame : IDisposable
{
    private byte[]? _buffer;

    internal Frame(FrameType type, byte[] buffer, int length)
    {
        Type = type;
        _buffer = buffer;
        Length = length;
    }

    public FrameType Type { get; }
    public int Length { get; }

    public ReadOnlySpan<byte> Payload => _buffer is null ? throw new ObjectDisposedException(nameof(Frame)) : _buffer.AsSpan(0, Length);

    public ReadOnlyMemory<byte> PayloadMemory => _buffer is null ? throw new ObjectDisposedException(nameof(Frame)) : _buffer.AsMemory(0, Length);

    public T Parse<T>(JsonTypeInfo<T> info) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(Payload, info) ?? throw new ProtocolException($"{Type}: contenido vacío.");
        }
        catch (JsonException ex)
        {
            throw new ProtocolException($"{Type}: JSON inválido ({ex.Message}).");
        }
    }

    public void Dispose()
    {
        var b = Interlocked.Exchange(ref _buffer, null);
        if (b is not null)
            ArrayPool<byte>.Shared.Return(b);
    }
}

/// <summary>
/// Tramas sobre un flujo TLS: <c>[u32 longitud][u8 tipo][contenido]</c>, con límites estrictos por tipo.
/// Las escrituras se serializan; las lecturas las hace un único lector.
/// </summary>
public sealed class FrameChannel
{
    public const int HeaderSize = 5;
    public const int MaxControlPayload = 1 << 20;
    public const int MaxOfferPayload = 16 << 20;
    public const int MaxChunkPayload = 1 << 20;

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _header = new byte[HeaderSize];

    public FrameChannel(Stream stream) => _stream = stream;

    public static int MaxPayloadFor(FrameType type) => type switch
    {
        FrameType.Offer => MaxOfferPayload,
        FrameType.Chunk => MaxChunkPayload,
        _ => MaxControlPayload,
    };

    /// <summary>Lee la siguiente trama; <c>null</c> si el otro lado cerró limpiamente entre tramas.</summary>
    public async Task<Frame?> ReadOrEndAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            if (!await ReadExactOrEndAsync(_header, cts.Token).ConfigureAwait(false))
                return null;
            var length = BinaryPrimitives.ReadUInt32BigEndian(_header);
            var rawType = _header[4];
            if (!Enum.IsDefined((FrameType)rawType))
                throw new ProtocolException($"Tipo de trama desconocido: {rawType}.");
            var type = (FrameType)rawType;
            if (length > (uint)MaxPayloadFor(type))
                throw new ProtocolException($"{type}: trama demasiado grande ({length} bytes).");
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, (int)length));
            try
            {
                if (length > 0 && !await ReadExactOrEndAsync(buffer.AsMemory(0, (int)length), cts.Token).ConfigureAwait(false))
                    throw new EndOfStreamException("Conexión cerrada a mitad de una trama.");
                return new Frame(type, buffer, (int)length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("El compañero dejó de responder.");
        }
    }

    /// <exception cref="EndOfStreamException">El otro lado cerró la conexión (no es una violación del protocolo).</exception>
    public async Task<Frame> ReadAsync(TimeSpan timeout, CancellationToken ct) =>
        await ReadOrEndAsync(timeout, ct).ConfigureAwait(false) ?? throw new EndOfStreamException("Conexión cerrada.");

    /// <summary>Lee una trama que debe ser de uno de los tipos permitidos; cualquier otro es una violación.</summary>
    public async Task<Frame> ReadExpectAsync(TimeSpan timeout, CancellationToken ct, params FrameType[] allowed)
    {
        var frame = await ReadAsync(timeout, ct).ConfigureAwait(false);
        if (Array.IndexOf(allowed, frame.Type) < 0)
        {
            var type = frame.Type;
            frame.Dispose();
            throw new ProtocolException($"Mensaje inesperado: {type}.");
        }
        return frame;
    }

    public async Task SendAsync<T>(FrameType type, T message, JsonTypeInfo<T> info, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, info);
        if (json.Length > MaxPayloadFor(type))
            throw new TransferException(ErrorCodes.TooLarge, "Mensaje demasiado grande.");
        var buffer = ArrayPool<byte>.Shared.Rent(HeaderSize + json.Length);
        try
        {
            WriteHeader(buffer, type, json.Length);
            json.CopyTo(buffer.AsSpan(HeaderSize));
            await WriteLockedAsync(buffer.AsMemory(0, HeaderSize + json.Length), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Envía datos que ya están en <paramref name="buffer"/> a partir de <see cref="HeaderSize"/>.</summary>
    public Task SendChunkAsync(byte[] buffer, int payloadLength, CancellationToken ct)
    {
        if (payloadLength > MaxChunkPayload)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        WriteHeader(buffer, FrameType.Chunk, payloadLength);
        return WriteLockedAsync(buffer.AsMemory(0, HeaderSize + payloadLength), ct);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void WriteHeader(byte[] buffer, FrameType type, int length)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)length);
        buffer[4] = (byte)type;
    }

    private async Task WriteLockedAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> ReadExactOrEndAsync(Memory<byte> buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            var n = await _stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                    return false;
                throw new EndOfStreamException("Conexión cerrada a mitad de una trama.");
            }
            read += n;
        }
        return true;
    }
}
