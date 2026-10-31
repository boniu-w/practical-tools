using System.Runtime.InteropServices;
using SkiaSharp;

namespace PracticalTools.Desktop.Preview;

/// <summary>单页 PDF 的渲染结果，包含总页数、像素尺寸和 PNG 数据；释放时清零图像缓冲。</summary>
public sealed record PdfPageImage(int PageCount, int Width, int Height, byte[] Png) : IDisposable
{
    /// <summary>清零 PNG 数据，避免预览关闭后仍保留页面内容。</summary>
    public void Dispose() => Array.Clear(Png);
}

/// <summary>串行执行 PDFium 光栅化；不启用表单、动作、JavaScript 或网络回调。</summary>
public sealed class PdfRenderer
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>在后台线程串行渲染 PDF 的指定页并编码为 PNG。</summary>
    /// <param name="pdf">PDF 文件内容，长度限制为 64 MiB。</param>
    /// <param name="page">从 0 开始的页码。</param>
    /// <param name="zoom">缩放百分比，允许 50–200；100 表示 96 DPI。</param>
    /// <param name="token">取消标记，在渲染前后检查。</param>
    /// <returns>该页的 PNG 数据、总页数和像素尺寸。</returns>
    /// <exception cref="InvalidDataException">PDF 为空、过大、损坏，或页数/尺寸超过限制。</exception>
    /// <exception cref="ArgumentOutOfRangeException">页码或缩放值无效。</exception>
    public Task<PdfPageImage> RenderAsync(byte[] pdf, int page, int zoom, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (pdf.Length == 0 || pdf.Length > 64 * 1024 * 1024) throw new InvalidDataException("PDF 大小必须在 1 字节到 64 MiB 之间。");
        if (page < 0 || zoom is < 50 or > 200) throw new ArgumentOutOfRangeException(nameof(page));
        // 复制一份输入，避免视图关闭时清空缓冲，而原生渲染尚未结束。
        var owned = pdf.ToArray();
        return Task.Run(() =>
        {
            try { lock (Gate) { token.ThrowIfCancellationRequested(); return Render(owned, page, zoom, token); } }
            finally { Array.Clear(owned); }
        }); // 不取消调度：委托必须执行，以便清理复制的缓冲。
    }

    /// <summary>
    /// 在 <see cref="Gate"/> 锁内调用 PDFium 渲染单页。
    /// PDFium 全局只初始化一次，所有原生对象在 finally 中释放。
    /// </summary>
    private static PdfPageImage Render(byte[] pdf, int index, int zoom, CancellationToken token)
    {
        if (!_initialized) { Native.FPDF_InitLibrary(); _initialized = true; }
        var pin = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        IntPtr document = IntPtr.Zero, page = IntPtr.Zero, bitmap = IntPtr.Zero;
        try
        {
            document = Native.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (nuint)pdf.Length, IntPtr.Zero);
            if (document == IntPtr.Zero) throw new InvalidDataException("无法打开 PDF：文件损坏、格式不受支持或需要 PDF 打开口令。请使用文件解密后查看。");
            int count = Native.FPDF_GetPageCount(document);
            if (count is <= 0 or > 2000) throw new InvalidDataException("PDF 页数必须在 1 到 2000 之间。");
            if (index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            token.ThrowIfCancellationRequested();
            page = Native.FPDF_LoadPage(document, index);
            if (page == IntPtr.Zero) throw new InvalidDataException("无法加载此 PDF 页面。");
            double width = Native.FPDF_GetPageWidthF(page), height = Native.FPDF_GetPageHeightF(page);
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                throw new InvalidDataException("PDF 页面尺寸无效。");
            // 100% 对应 96 DPI；分配原生位图前先限制光栅尺寸。
            double scale = zoom / 100d * 96 / 72;
            double w = Math.Ceiling(width * scale), h = Math.Ceiling(height * scale);
            if (w > 8192 || h > 8192 || w * h > 16_000_000)
                throw new InvalidDataException("页面在当前缩放下超过 1600 万像素或单边 8192 像素，请降低缩放。");
            using var pixels = new SKBitmap((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            pixels.Erase(SKColors.White);
            bitmap = Native.FPDFBitmap_CreateEx((int)w, (int)h, 4, pixels.GetPixels(), pixels.RowBytes);
            if (bitmap == IntPtr.Zero) throw new InvalidDataException("无法创建 PDF 页面图像。");
            try { Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, (int)w, (int)h, 0, 1); } // 仅静态页面内容，不渲染交互注释。
            finally { Native.FPDFBitmap_Destroy(bitmap); bitmap = IntPtr.Zero; }
            token.ThrowIfCancellationRequested();
            using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
            // 编码完成后再检查一次取消，并清零准备丢弃的 PNG 数据。
            var bytes = encoded.ToArray();
            if (token.IsCancellationRequested) { Array.Clear(bytes); token.ThrowIfCancellationRequested(); }
            return new(count, (int)w, (int)h, bytes);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) Native.FPDFBitmap_Destroy(bitmap);
            if (page != IntPtr.Zero) Native.FPDF_ClosePage(page);
            if (document != IntPtr.Zero) Native.FPDF_CloseDocument(document);
            pin.Free();
        }
    }

    /// <summary>PDFium 原生函数声明。</summary>
    private static class Native
    {
        private const string Library = "pdfium";
        [DllImport(Library)] internal static extern void FPDF_InitLibrary();
        [DllImport(Library)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, IntPtr password);
        [DllImport(Library)] internal static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Library)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
        [DllImport(Library)] internal static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Library)] internal static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Library)] internal static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr buffer, int stride);
        [DllImport(Library)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
        [DllImport(Library)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library)] internal static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Library)] internal static extern void FPDF_CloseDocument(IntPtr document);
    }
}
