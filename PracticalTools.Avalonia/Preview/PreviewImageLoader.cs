using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace PracticalTools.Desktop.Preview;

/// <summary>预览解码结果：静态图使用位图，动画 GIF 使用数据流。</summary>
public sealed class PreviewImage
{
    /// <summary>静态图片位图；动画 GIF 时为空。</summary>
    public Bitmap? Bitmap { get; init; }
    /// <summary>动画 GIF 的数据流；静态图片时为空。</summary>
    public Stream? GifStream { get; init; }
    /// <summary>图片原始像素尺寸。</summary>
    public PixelSize Size { get; init; }
    /// <summary>降级或限制提示，例如动画超限时仅显示首帧。</summary>
    public string? Notice { get; init; }
    public bool IsAnimated => GifStream != null;
}

/// <summary>在 UI 线程之外解码预览图，并限制动画 GIF 的帧数与总像素。</summary>
public static class PreviewImageLoader
{
    public const int MaxPixels = 16_000_000;
    public const int MaxGifFrames = 300;
    public const long MaxGifTotalPixels = 64_000_000;
    private const int MaxDisplayWidth = 1600;

    /// <summary>按扩展名选择解码方式；动画 GIF 返回原始数据流，其余返回静态位图。</summary>
    /// <param name="bytes">图片文件内容。</param>
    /// <param name="extension">扩展名（含点），如 <c>.avif</c>、<c>.gif</c>。</param>
    /// <param name="token">取消标记。</param>
    /// <returns>位图或动画流，以及原始尺寸和提示信息。</returns>
    /// <exception cref="InvalidDataException">图片格式无效或超过像素限制。</exception>
    public static PreviewImage Load(byte[] bytes, string extension, CancellationToken token = default)
    {
        if (extension == ".avif") return LoadAvif(bytes, token);
        if (extension == ".gif")
        {
            var (animate, size, notice) = InspectGif(bytes);
            if (animate)
            {
                token.ThrowIfCancellationRequested();
                // PreviewContent 在此调用后会清空缓冲，因此动画持有一份副本。
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

    /// <summary>解码 AVIF 首帧为位图，显示宽度不超过 1600 像素。</summary>
    private static PreviewImage LoadAvif(byte[] bytes, CancellationToken token)
    {
        var avif = AvifPreviewDecoder.Decode(bytes, token);
        using var stream = new MemoryStream(avif.Png, writable: false);
        var bitmap = Bitmap.DecodeToWidth(stream, Math.Min(MaxDisplayWidth, avif.Width));
        return new PreviewImage { Bitmap = bitmap, Size = new PixelSize(avif.Width, avif.Height) };
    }

    /// <summary>用 SkiaSharp 解码静态图片并按显示宽度缩放；解码前先校验像素上限。</summary>
    private static PreviewImage LoadStatic(byte[] bytes, CancellationToken token)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("图片格式无效。");
        // 先读取编码信息校验限制，再解码，避免超大图片耗尽内存。
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("图片超过 1600 万像素预览限制。");
        token.ThrowIfCancellationRequested();
        using var bitmapStream = new MemoryStream(bytes, writable: false);
        var bitmap = Bitmap.DecodeToWidth(bitmapStream, Math.Min(MaxDisplayWidth, info.Width));
        return new PreviewImage { Bitmap = bitmap, Size = new PixelSize(info.Width, info.Height) };
    }

    /// <summary>
    /// 检查 GIF 是否为可播放动画：帧数大于 1、尺寸有效，且未超过帧数与总像素限制。
    /// 超限时返回尺寸和“仅显示首帧”的提示。
    /// </summary>
    /// <returns>是否使用动画、原始尺寸和超限提示。</returns>
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
            // GIF 信息读取失败时按静态图片处理，由静态解码路径给出统一错误。
            return (false, default, null);
        }
    }
}
