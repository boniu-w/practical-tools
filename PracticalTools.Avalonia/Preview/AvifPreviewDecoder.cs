using ImageMagick;

namespace PracticalTools.Desktop.Preview;

public sealed record AvifPreview(byte[] Png, int Width, int Height);

/// <summary>在受限的 ImageMagick 资源下解码 AVIF 首帧。</summary>
public static class AvifPreviewDecoder
{
    /// <summary>预览允许的最大解码像素数（1600 万）。</summary>
    public const int MaxPixels = 16_000_000;
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>将 AVIF 图片的首帧解码为 PNG 字节。</summary>
    /// <param name="bytes">AVIF 文件的编码内容；不会修改传入的缓冲区。</param>
    /// <param name="token">在解码前和转码为 PNG 前检查取消请求。</param>
    /// <returns>PNG 预览字节及源图尺寸。</returns>
    /// <exception cref="InvalidDataException">
    /// <paramref name="bytes"/> 为空、格式无效、不受支持，或像素数超过 <see cref="MaxPixels"/>。
    /// </exception>
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

    /// <summary>
    /// 仅执行一次进程级 ImageMagick 资源限制设置。禁用磁盘缓存，避免恶意文件写满临时盘；
    /// 单张图片解码保持单线程。
    /// </summary>
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
            // ImageMagick 内部操作需要不止一个图像列表项，因此不能设为 1。
            ResourceLimits.ListLength = 16;
            _initialized = true;
        }
    }
}
