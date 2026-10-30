using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using PracticalTools.Desktop.Views;
using PracticalTools.GPG;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class PreviewUiTests
{
    [AvaloniaFact]
    public async Task AudioPreviewControlsAndDetachReleasePlayback()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("audio", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var source = store.Output("audio.wav"); File.WriteAllBytes(source, PracticalTools.TestFixtures.MediaFixture.Wave());
        var input = store.Output("renamed.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, await store.Library.GetPublicKeyAsync(key.Id));
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var media = page.FindControl<MediaPreviewView>("MediaView")!;
        PracticalTools.Desktop.Preview.MediaPlayback? session = null;
        media.CreatePlayback = (data, demux) => session = new(data, demux, silent: true);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            page.FindControl<TextBox>("InputPath")!.Text = input;
            Click(page, "RunButton"); await Finished(page);
            Assert.True(media.IsVisible); Assert.NotNull(session);
            Assert.NotEqual(LibVLCSharp.Shared.VLCState.Playing, session!.Player.State);
            Click(media, "PlayButton");
            var end = DateTime.UtcNow.AddSeconds(10);
            while (session.Player.State != LibVLCSharp.Shared.VLCState.Playing)
            { if (DateTime.UtcNow > end) throw new TimeoutException(); await Task.Delay(30); }
            Assert.True(session.Player.EnableHardwareDecoding);
            media.FindControl<CheckBox>("DisableHardwareDecoding")!.IsChecked = true;
            Assert.False(session.Player.EnableHardwareDecoding);
            Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, session.Player.State);
            Click(media, "PlayButton");
            end = DateTime.UtcNow.AddSeconds(10);
            while (session.Player.State != LibVLCSharp.Shared.VLCState.Playing)
            { if (DateTime.UtcNow > end) throw new TimeoutException(); await Task.Delay(30); }
            media.FindControl<CheckBox>("DisableHardwareDecoding")!.IsChecked = false;
            Assert.True(session.Player.EnableHardwareDecoding);
            Click(media, "PauseButton"); Click(media, "StopButton");
            Assert.Equal(LibVLCSharp.Shared.VLCState.Stopped, session.Player.State);
            window.Content = null;
            Assert.Throws<ObjectDisposedException>(() => session.Play());
            Assert.Single(Directory.GetDirectories(store.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PdfPagesZoomAndCloseReleasePreviewWithoutLatePublication()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("pdf", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var source = store.Output("document.pdf");
        File.WriteAllBytes(source, PracticalTools.TestFixtures.PdfFixture.Create());
        var input = store.Output("renamed.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, await store.Library.GetPublicKeyAsync(key.Id));
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            page.FindControl<TextBox>("InputPath")!.Text = input;
            Click(page, "RunButton"); await Finished(page);
            var image = page.FindControl<Image>("ImagePreview")!;
            Assert.NotNull(image.Source); Assert.Equal("1 / 2", page.FindControl<TextBlock>("PdfPosition")!.Text);
            Assert.False(page.FindControl<Button>("PreviousPage")!.IsEnabled);
            Click(page, "NextPage"); await Finished(page);
            Assert.Equal("2 / 2", page.FindControl<TextBlock>("PdfPosition")!.Text);
            Assert.False(page.FindControl<Button>("NextPage")!.IsEnabled);
            var width = image.Width; Click(page, "ZoomIn"); await Finished(page);
            Assert.True(image.Width > width); Assert.Equal("125%", page.FindControl<TextBlock>("PdfZoom")!.Text);
            Click(page, "PreviousPage"); Click(page, "CloseButton"); await Finished(page);
            Assert.Null(image.Source); Assert.False(page.FindControl<StackPanel>("PdfControls")!.IsVisible);
            Click(page, "RunButton"); await Finished(page);
            window.Content = null; Assert.Null(image.Source);
            Assert.Single(Directory.GetDirectories(store.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task WorkbookSwitchesSheetsAndClearsAllSheetContentOnCloseAndDetach()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("workbook", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var source = store.Output("book.xlsx");
        File.WriteAllBytes(source, PracticalTools.TestFixtures.XlsxFixture.Build());
        var input = store.Output("renamed.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, await store.Library.GetPublicKeyAsync(key.Id));
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            page.FindControl<TextBox>("InputPath")!.Text = input;
            Click(page, "RunButton"); await Finished(page);
            var selector = page.FindControl<ComboBox>("SheetSelector")!;
            var text = page.FindControl<TextBox>("TextPreview")!;
            Assert.Equal(2, selector.ItemCount); Assert.Contains("A1\t中文内容", text.Text);
            selector.SelectedIndex = 1; Assert.Equal("空工作表", text.Text);
            selector.SelectedIndex = 0; Assert.Contains("公式缓存值", text.Text);
            Click(page, "CloseButton"); Assert.Null(selector.ItemsSource); Assert.True(string.IsNullOrEmpty(text.Text));
            Click(page, "RunButton"); await Finished(page);
            window.Content = null; Assert.Null(selector.ItemsSource); Assert.True(string.IsNullOrEmpty(text.Text));
            Assert.Single(Directory.GetDirectories(store.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImagePreviewAndInvalidImageRestoreControls(bool invalid)
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("image", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var source = store.Output("preview.png");
        using (var image = new SkiaSharp.SKBitmap(2, 2))
        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(source, invalid ? [1, 2, 3] : data.ToArray());
        var input = store.Output("preview.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, await store.Library.GetPublicKeyAsync(key.Id));
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            page.FindControl<TextBox>("InputPath")!.Text = input;
            Click(page, "RunButton"); await Finished(page);
            var preview = page.FindControl<Image>("ImagePreview")!;
            if (invalid) { Assert.Null(preview.Source); Assert.Contains("无法预览", page.FindControl<TextBlock>("Status")!.Text); }
            else Assert.NotNull(preview.Source);
            Click(page, "CloseButton"); Assert.Null(preview.Source);
            Assert.Single(Directory.GetDirectories(store.Source));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AdditionalImageFormatsAndAnimatedGifRender()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("images", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var publicKey = await store.Library.GetPublicKeyAsync(key.Id);
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            foreach (var (name, bytes) in new[]
                     {
                         ("preview.bmp", PracticalTools.TestFixtures.ImageFixture.Bmp()),
                         ("preview.gif", PracticalTools.TestFixtures.ImageFixture.Gif()),
                         ("preview.ico", PracticalTools.TestFixtures.ImageFixture.Ico()),
                         ("preview.webp", PracticalTools.TestFixtures.ImageFixture.Webp()),
                         ("preview.avif", PracticalTools.TestFixtures.ImageFixture.Avif())
                     })
            {
                var source = store.Output(name); File.WriteAllBytes(source, bytes);
                var input = store.Output("preview.gpg");
                await GpgHelper.EncryptFileByPublicString(source, input, publicKey);
                page.FindControl<TextBox>("InputPath")!.Text = input;
                Click(page, "RunButton"); await Finished(page);
                Assert.NotNull(page.FindControl<Image>("ImagePreview")!.Source);
                Assert.True(page.FindControl<ScrollViewer>("ImageHost")!.IsVisible);
                Assert.False(page.FindControl<Grid>("GifHost")!.IsVisible);
            }

            var gifSource = store.Output("animated.gif");
            File.WriteAllBytes(gifSource, PracticalTools.TestFixtures.ImageFixture.AnimatedGif());
            var gifInput = store.Output("preview.gpg");
            await GpgHelper.EncryptFileByPublicString(gifSource, gifInput, publicKey);
            page.FindControl<TextBox>("InputPath")!.Text = gifInput;
            Click(page, "RunButton"); await Finished(page);
            Assert.Null(page.FindControl<Image>("ImagePreview")!.Source);
            Assert.True(page.FindControl<Grid>("GifHost")!.IsVisible);
            Assert.Single(page.FindControl<Grid>("GifHost")!.Children);
            Assert.True(page.FindControl<ScrollViewer>("ImageHost")!.IsVisible);

            Click(page, "CloseButton");
            Assert.Null(page.FindControl<Image>("ImagePreview")!.Source);
            Assert.False(page.FindControl<Grid>("GifHost")!.IsVisible);
            Assert.Empty(page.FindControl<Grid>("GifHost")!.Children);
            Assert.Single(Directory.GetDirectories(store.Source));
        }
        finally { window.Close(); }
    }

    private static void Click(Control page, string name) => page.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Finished(DecryptionPreviewPage page)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!page.FindControl<StackPanel>("Inputs")!.IsEnabled)
        { if (DateTime.UtcNow > timeout) throw new TimeoutException(); await Task.Delay(20); }
    }
    [AvaloniaFact]
    public async Task StoredKeyPreviewClearsOnClosePathChangeAndDetach()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("preview", "password", 2048, 0);
        var key = (await store.Library.ListAsync()).Single();
        var source = store.Output("preview.txt"); File.WriteAllText(source, "中文 <script>passive</script>");
        var input = store.Output("preview.gpg");
        await GpgHelper.EncryptFileByPublicString(source, input, await store.Library.GetPublicKeyAsync(key.Id));
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            var path = page.FindControl<TextBox>("InputPath")!; path.Text = input;
            var preview = page.FindControl<TextBox>("TextPreview")!;
            Click(page, "RunButton"); await Finished(page);
            Assert.Equal("中文 <script>passive</script>", preview.Text);
            Assert.Single(Directory.GetDirectories(store.Source)); // Only fixture's original empty directory.
            Click(page, "CloseButton"); Assert.True(string.IsNullOrEmpty(preview.Text));
            Click(page, "RunButton"); await Finished(page); path.Text = "different.gpg";
            Assert.True(string.IsNullOrEmpty(preview.Text));
            path.Text = input; Click(page, "RunButton"); await Finished(page);
            window.Content = null; Assert.True(string.IsNullOrEmpty(preview.Text));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task ImmediateCancelDoesNotPublishLateResult()
    {
        using var store = new FolderCryptoUiTests.Store();
        await store.Library.GenerateAsync("preview", "password", 2048, 0);
        var page = new DecryptionPreviewPage(); page.Configure(store.Library, store.Source);
        var window = new Window { Content = page }; window.Show();
        try
        {
            await page.ReloadKeysAsync(); page.FindControl<ComboBox>("LibraryKeys")!.SelectedIndex = 0;
            Click(page, "RunButton"); Click(page, "CloseButton"); await Finished(page);
            Assert.True(string.IsNullOrEmpty(page.FindControl<TextBox>("TextPreview")!.Text));
            Assert.Contains("取消", page.FindControl<TextBlock>("Status")!.Text);
        }
        finally { window.Close(); }
    }
}
