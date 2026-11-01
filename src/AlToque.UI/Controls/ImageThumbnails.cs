using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace AlToque.Controls;

/// <summary>
/// Fotos para el chat y el visor: se decodifican a la medida justa (JPEG y WebP sin cargar la foto entera), con la
/// orientación de la cámara aplicada. Las que no son imágenes razonables (rotas, gigantes, bombas de píxeles) no se
/// muestran: quedan como un archivo más.
/// </summary>
public static class ImageThumbnails
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    private const long MaxFileBytes = 100L * 1024 * 1024;

    /// <summary>Tamaño declarado máximo (una foto de 200 MP pasa; un PNG de 100 000 × 100 000 no).</summary>
    private const long MaxPixels = 200_000_000;

    /// <summary>Lo que se llega a decodificar de una vez (los formatos que no se reducen al leer).</summary>
    private const long MaxDecodedPixels = 40_000_000;

    /// <summary>Memoria para las imágenes ya decodificadas (las del visor pesan mucho más que las miniaturas).</summary>
    private const long CacheBytes = 96L * 1024 * 1024;

    private static readonly Lock CacheLock = new();
    private static readonly LinkedList<(string Key, Bitmap Bitmap, long Bytes)> Cache = new();
    private static long _cachedBytes;

    // Una o dos a la vez: abrir un chat con 50 fotos no debe disparar la memoria.
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public static bool IsImage(string? nameOrPath) => !string.IsNullOrEmpty(nameOrPath) && Extensions.Contains(Path.GetExtension(nameOrPath));

    /// <summary>La imagen con su lado mayor en <paramref name="maxSide"/> píxeles como mucho, o null si no se puede mostrar.</summary>
    public static async Task<Bitmap?> LoadAsync(string path, int maxSide)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(path, maxSide)).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Bitmap? Load(string path, int maxSide)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > MaxFileBytes || !IsImage(path))
                return null;
            var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{maxSide}";
            lock (CacheLock)
            {
                for (var node = Cache.First; node is not null; node = node.Next)
                {
                    if (node.Value.Key == key)
                    {
                        Cache.Remove(node);
                        Cache.AddFirst(node);
                        return node.Value.Bitmap;
                    }
                }
            }
            var bitmap = Decode(file.FullName, maxSide);
            if (bitmap is not null)
            {
                var bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                lock (CacheLock)
                {
                    Cache.AddFirst((key, bitmap, bytes));
                    _cachedBytes += bytes;
                    // Se suelta lo menos usado; la imagen sigue viva mientras algo la muestre.
                    while (_cachedBytes > CacheBytes && Cache.Count > 1)
                    {
                        _cachedBytes -= Cache.Last!.Value.Bytes;
                        Cache.RemoveLast();
                    }
                }
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static Bitmap? Decode(string path, int maxSide)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            return null;
        var scale = Math.Min(1f, maxSide / (float)Math.Max(info.Width, info.Height));
        // Lo que el decodificador sabe hacer directo (JPEG: 1/2, 1/4, 1/8; WebP: cualquier tamaño).
        var native = codec.GetScaledDimensions(scale);
        if (native.Width <= 0 || native.Height <= 0 || (long)native.Width * native.Height > MaxDecodedPixels)
            return null;
        var decodeInfo = new SKImageInfo(native.Width, native.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            return null;

        var width = Math.Max(1, (int)Math.Round(info.Width * scale));
        var height = Math.Max(1, (int)Math.Round(info.Height * scale));
        using var sized = native.Width == width && native.Height == height
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (sized is null)
            return null;
        using var oriented = Orient(sized, codec.EncodedOrigin);
        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, oriented.GetPixels(), new PixelSize(oriented.Width, oriented.Height),
            new Vector(96, 96), oriented.RowBytes);
    }

    /// <summary>Gira o refleja según la etiqueta EXIF de la cámara (las fotos del teléfono suelen venir "de lado").</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width, h = source.Height;
        // x' = ScaleX·x + SkewX·y + TransX ; y' = SkewY·x + ScaleY·y + TransY
        SKMatrix? matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (matrix is not { } m)
            return source.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(m);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return output;
    }
}
