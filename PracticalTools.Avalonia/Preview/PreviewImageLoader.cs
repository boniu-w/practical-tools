using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace PracticalTools.Desktop.Preview;

public sealed class PreviewImage
{
    public Bitmap? Bitmap { get; init; }
    public Stream? GifStream { get; init; }
    public PixelSize Size { get; init; }
    public string? Notice { get; init; }
    public bool IsAnimated => GifStream != null;
}

/// <summary>Decodes preview images off the UI thread and keeps animated GIFs in bounds.</summary>
public static class PreviewImageLoader
{
    public const int MaxPixels = 16_000_000;
    public const int MaxGifFrames = 300;
    public const long MaxGifTotalPixels = 64_000_000;
    private const int MaxDisplayWidth = 1600;

    public static PreviewImage Load(byte[] bytes, string extension, CancellationToken token = default)
    {
        if (extension == ".avif") return LoadAvif(bytes, token);
        if (extension == ".gif")
        {
            var (animate, size, notice) = InspectGif(bytes);
            if (animate)
            {
                token.ThrowIfCancellationRequested();
                // PreviewContent clears its buffer after this call, so the animation owns a copy.
                return new PreviewImage
                {
                    GifStream = new MemoryStream(bytes.ToArray(), writable: false),
                    Size = size
                };
            }
            var still = LoadStatic(bytes, token);
            return new PreviewImage { Bitmap = still.Bitmap, Size = still.Size, Notice = notice };
        }
        return LoadStatic(bytes, token);
    }

    private static PreviewImage LoadAvif(byte[] bytes, CancellationToken token)
    {
        var avif = AvifPreviewDecoder.Decode(bytes, token);
        using var stream = new MemoryStream(avif.Png, writable: false);
        var bitmap = Bitmap.DecodeToWidth(stream, Math.Min(MaxDisplayWidth, avif.Width));
        return new PreviewImage { Bitmap = bitmap, Size = new PixelSize(avif.Width, avif.Height) };
    }

    private static PreviewImage LoadStatic(byte[] bytes, CancellationToken token)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("图片格式无效。");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("图片超过 1600 万像素预览限制。");
        token.ThrowIfCancellationRequested();
        using var bitmapStream = new MemoryStream(bytes, writable: false);
        var bitmap = Bitmap.DecodeToWidth(bitmapStream, Math.Min(MaxDisplayWidth, info.Width));
        return new PreviewImage { Bitmap = bitmap, Size = new PixelSize(info.Width, info.Height) };
    }

    private static (bool Animate, PixelSize Size, string? Notice) InspectGif(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec == null || codec.FrameCount <= 1) return (false, default, null);
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return (false, default, null);
            var size = new PixelSize(info.Width, info.Height);
            if (codec.FrameCount > MaxGifFrames ||
                (long)info.Width * info.Height * codec.FrameCount > MaxGifTotalPixels)
                return (false, size, "动画帧数或总像素超过预览限制，仅显示首帧。");
            return (true, size, null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return (false, default, null);
        }
    }
}
