using System.Runtime.InteropServices;
using SkiaSharp;

namespace PracticalTools.Desktop.Preview;

public sealed record PdfPageImage(int PageCount, int Width, int Height, byte[] Png) : IDisposable
{
    public void Dispose() => Array.Clear(Png);
}

/// <summary>Serialized PDFium rasterization; no forms, actions, JavaScript or network callbacks.</summary>
public sealed class PdfRenderer
{
    private static readonly object Gate = new();
    private static bool _initialized;
    public Task<PdfPageImage> RenderAsync(byte[] pdf, int page, int zoom, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (pdf.Length == 0 || pdf.Length > 64 * 1024 * 1024) throw new InvalidDataException("PDF 大小必须在 1 字节到 64 MiB 之间。");
        if (page < 0 || zoom is < 50 or > 200) throw new ArgumentOutOfRangeException(nameof(page));
        // Own a copy so closing the view may clear its buffer while native rendering finishes.
        var owned = pdf.ToArray();
        return Task.Run(() =>
        {
            try { lock (Gate) { token.ThrowIfCancellationRequested(); return Render(owned, page, zoom, token); } }
            finally { Array.Clear(owned); }
        }); // Do not cancel scheduling: the delegate must execute its buffer cleanup.
    }

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
            // 100% = 96 DPI. Bound raster size before native allocation.
            double scale = zoom / 100d * 96 / 72;
            double w = Math.Ceiling(width * scale), h = Math.Ceiling(height * scale);
            if (w > 8192 || h > 8192 || w * h > 16_000_000)
                throw new InvalidDataException("页面在当前缩放下超过 1600 万像素或单边 8192 像素，请降低缩放。");
            using var pixels = new SKBitmap((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            pixels.Erase(SKColors.White);
            bitmap = Native.FPDFBitmap_CreateEx((int)w, (int)h, 4, pixels.GetPixels(), pixels.RowBytes);
            if (bitmap == IntPtr.Zero) throw new InvalidDataException("无法创建 PDF 页面图像。");
            try { Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, (int)w, (int)h, 0, 1); } // Static annotations only.
            finally { Native.FPDFBitmap_Destroy(bitmap); bitmap = IntPtr.Zero; }
            token.ThrowIfCancellationRequested();
            using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
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
