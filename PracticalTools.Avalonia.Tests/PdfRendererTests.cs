using PracticalTools.Desktop.Preview;
using PracticalTools.TestFixtures;
using SkiaSharp;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class PdfRendererTests
{
    [Fact]
    public async Task NativeRenderingPreservesPageColorsAndZoomAndReleasesOutput()
    {
        var renderer = new PdfRenderer(); var pdf = PdfFixture.Create();
        using var first = await renderer.RenderAsync(pdf, 0, 100);
        using var second = await renderer.RenderAsync(pdf, 1, 200);
        Assert.Equal(2, first.PageCount); Assert.Equal(267, first.Width); Assert.Equal(134, first.Height);
        Assert.Equal(534, second.Width); Assert.Equal(267, second.Height);
        using var red = SKBitmap.Decode(first.Png); using var blue = SKBitmap.Decode(second.Png);
        Assert.Equal(SKColors.Red, red.GetPixel(30, 30)); Assert.Equal(SKColors.Blue, blue.GetPixel(30, 30));
        first.Dispose(); Assert.All(first.Png, b => Assert.Equal(0, b));
    }
    [Theory]
    [InlineData("broken")]
    [InlineData("page")]
    [InlineData("size")]
    [InlineData("count")]
    public async Task InvalidDocumentsAndResourceLimitsFailWithoutBreakingLaterRender(string kind)
    {
        var renderer = new PdfRenderer();
        var pdf = kind switch { "broken" => new byte[] { 1, 2, 3 }, "size" => PdfFixture.Create(width: 100000), "count" => PdfFixture.Create(pages: 2001), _ => PdfFixture.Create() };
        await Assert.ThrowsAnyAsync<Exception>(async () => { using var result = await renderer.RenderAsync(pdf, kind == "page" ? 2 : 0, 100); });
        using var valid = await renderer.RenderAsync(PdfFixture.Create(), 0, 50); Assert.Equal(2, valid.PageCount);
    }
    [Fact]
    public async Task CancellationAndConcurrentRenderingDoNotRetainCallerBuffer()
    {
        var renderer = new PdfRenderer(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var result = await renderer.RenderAsync(PdfFixture.Create(), 0, 100, cts.Token); });
        var input = PdfFixture.Create();
        var first = renderer.RenderAsync(input, 0, 100); var second = renderer.RenderAsync(input, 1, 100);
        Array.Clear(input);
        using var red = await first; using var blue = await second;
        Assert.Equal(2, red.PageCount); Assert.Equal(2, blue.PageCount);
    }
}
