using ImageMagick;

namespace PracticalTools.Desktop.Preview;

public sealed record AvifPreview(byte[] Png, int Width, int Height);

/// <summary>Decodes the first AVIF frame with bounded ImageMagick resources.</summary>
public static class AvifPreviewDecoder
{
    public const int MaxPixels = 16_000_000;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static AvifPreview Decode(byte[] bytes, CancellationToken token = default)
    {
        if (bytes.Length == 0) throw new InvalidDataException("图片格式无效。");
        Initialize();
        token.ThrowIfCancellationRequested();
        try
        {
            using var image = new MagickImage(bytes, new MagickReadSettings
            {
                Format = MagickFormat.Avif,
                FrameIndex = 0,
                FrameCount = 1
            });
            token.ThrowIfCancellationRequested();
            if (image.Width == 0 || image.Height == 0 || (long)image.Width * image.Height > MaxPixels)
                throw new InvalidDataException("图片超过 1600 万像素预览限制。");
            byte[] png = image.ToByteArray(MagickFormat.Png);
            return new AvifPreview(png, (int)image.Width, (int)image.Height);
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException("图片格式无效或不受支持。", ex);
        }
    }

    private static void Initialize()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            ResourceLimits.Memory = 256UL * 1024 * 1024;
            ResourceLimits.Disk = 0;
            ResourceLimits.MaxMemoryRequest = 64UL * 1024 * 1024;
            ResourceLimits.Width = 16384;
            ResourceLimits.Height = 16384;
            ResourceLimits.Area = MaxPixels;
            ResourceLimits.Thread = 1;
            // One image list entry is too low for ImageMagick's internal operations.
            ResourceLimits.ListLength = 16;
            _initialized = true;
        }
    }
}
