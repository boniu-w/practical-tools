using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PracticalTools.GPG;
using PracticalTools.Sqlite;
using PracticalTools.Views;
using PracticalTools.vo;

namespace TestProject1;

[TestClass]
public class DecryptionPreviewUiTest
{
    [TestMethod]
    [TestCategory("UI")]
    public void EmbeddedViewerLoadsDocumentsAndPlaysVideo()
    {
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { Assert.Inconclusive("UI 测试需要 WebView2 Runtime。"); }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new CustomColorTheme
                {
                    BaseTheme = BaseTheme.Light,
                    PrimaryColor = Color.FromRgb(0, 115, 185),
                    SecondaryColor = Color.FromRgb(58, 126, 0)
                });
                foreach (string name in new[] { "MaterialDesignTheme.Light.xaml", "MaterialDesign3.Defaults.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/" + name)
                    });
                var window = new Window
                {
                    Title = "PracticalTools preview integration test",
                    Width = 1050, Height = 650, Left = -3000, Top = -3000,
                    ShowInTaskbar = false
                };
                window.Loaded += async (_, _) =>
                {
                    try { await ExerciseViewerAsync(window); }
                    catch (Exception ex) { failure = ex; }
                    finally { window.Close(); app.Shutdown(); }
                };
                app.Run(window);
            }
            catch (Exception ex) { failure = ex; app?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(90)), "预览 UI 测试超时。");
        if (failure != null) throw new AssertFailedException("预览 UI 测试失败：" + failure);
    }

    private static async Task ExerciseViewerAsync(Window window)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PracticalToolsPreviewUiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SQLitePCL.Batteries_V2.Init();
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            using var db = new MyDbContext(new DbContextOptionsBuilder<MyDbContext>().UseSqlite(connection).Options);
            db.Database.EnsureCreated();
            var (pub, sec, _, _) = GpgHelper.GeneratePgpKeyPair("ui-preview@example.invalid", "ui-test");
            var key = new GpgFile
            {
                PublicKey = GpgHelper.ExportPublicKey(pub), SecretKey = GpgHelper.ExportPrivateKey(sec),
                Passphrase = "ui-test", FileNamePublic = "UI 测试密钥"
            };
            var wrapper = new GpgFileEntryWrapper(key);
            var vm = new MainViewModel(db) { GpgFileWrapperList = new ObservableCollection<GpgFileEntryWrapper> { wrapper } };
            vm.WrapperSelectedChangedEvent();
            wrapper.IsSelected = true;
            var page = new DecryptionPreviewPage { DataContext = vm };
            window.Content = page;
            string textPath = Path.Combine(directory, "中文.txt");
            await File.WriteAllTextAsync(textPath, "查看加密文件 UI 正文 <script>不可执行</script>");
            var browser = await OpenAsync(page, textPath, key);
            string text = await browser.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            StringAssert.Contains(JsonSerializer.Deserialize<string>(text)!, "查看加密文件 UI 正文");

            // 用浏览器自身生成短 WebM 测试片段，避免依赖外部媒体或额外编码工具。
            var fixture = new TaskCompletionSource<string>();
            void Receive(object? _, CoreWebView2WebMessageReceivedEventArgs args)
            {
                string message = args.TryGetWebMessageAsString();
                if (message.StartsWith("fixture:")) fixture.TrySetResult(message[8..]);
            }
            browser.CoreWebView2.WebMessageReceived += Receive;
            await browser.CoreWebView2.ExecuteScriptAsync("""
                (()=>{const c=document.createElement('canvas');c.width=96;c.height=64;
                const ctx=c.getContext('2d');ctx.fillStyle='#0073b9';ctx.fillRect(0,0,96,64);
                let frame=0;const timer=setInterval(()=>{
                ctx.fillStyle=frame++%2?'#0073b9':'#3a7e00';ctx.fillRect(0,0,96,64);},50);
                const stream=c.captureStream(10),r=new MediaRecorder(stream,{mimeType:'video/webm;codecs=vp8'}),chunks=[];
                r.ondataavailable=e=>chunks.push(e.data);r.onstop=()=>{clearInterval(timer);stream.getTracks().forEach(t=>t.stop());
                const reader=new FileReader();reader.onload=()=>window.chrome.webview.postMessage('fixture:'+reader.result.split(',')[1]);
                reader.readAsDataURL(new Blob(chunks,{type:'video/webm'}));};r.start();setTimeout(()=>r.stop(),1200);})();
                """);
            string videoBase64 = await fixture.Task.WaitAsync(TimeSpan.FromSeconds(10));
            browser.CoreWebView2.WebMessageReceived -= Receive;
            string videoPath = Path.Combine(directory, "测试视频.webm");
            await File.WriteAllBytesAsync(videoPath, Convert.FromBase64String(videoBase64));
            string previousSession = Path.GetDirectoryName(browser.CreationProperties.UserDataFolder)!;
            browser = await OpenAsync(page, videoPath, key);
            Assert.IsFalse(Directory.Exists(Path.Combine(previousSession, "content")), "切换文件后应清理上一份临时副本。");
            await UntilAsync(async () =>
            {
                string state = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').readyState");
                return int.TryParse(state, out int ready) && ready >= 2;
            });
            await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').play();");
            await UntilAsync(async () => double.TryParse(await browser.CoreWebView2.ExecuteScriptAsync(
                "document.querySelector('video').currentTime"), CultureInfo.InvariantCulture, out double time) && time > 0);

            string pdfPath = Path.Combine(directory, "示例.pdf");
            WritePdfFixture(pdfPath);
            browser = await OpenAsync(page, pdfPath, key);
            Assert.IsTrue(browser.CoreWebView2.Source.EndsWith("preview.pdf"));
            string contentType = await browser.CoreWebView2.ExecuteScriptAsync("document.contentType");
            StringAssert.Contains(contentType, "application/pdf");

            string activeSession = Path.GetDirectoryName(browser.CreationProperties.UserDataFolder)!;
            window.Content = null; // 离开页签时必须释放浏览器和明文。
            await UntilAsync(() => Task.FromResult(!Directory.Exists(activeSession)));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<WebView2> OpenAsync(DecryptionPreviewPage page, string path, GpgFile key)
    {
        await GpgHelper.EncryptFileByPublicString(path, path + ".gpg", key.PublicKey);
        page.SetEncryptedFile(path + ".gpg");
        var button = (Button)page.FindName("PreviewButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var status = (TextBlock)page.FindName("StatusText");
        await UntilAsync(() => Task.FromResult(button.IsEnabled &&
            (status.Text.StartsWith("正在预览：") || status.Text.Contains("失败") || status.Text.Contains("无法"))));
        Assert.IsTrue(status.Text.StartsWith("正在预览："), status.Text);
        return ((Grid)page.FindName("BrowserHost")).Children.OfType<WebView2>().Single();
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > timeout) Assert.Fail("等待内嵌预览超时。");
            await Task.Delay(50);
        }
    }

    private static void WritePdfFixture(string path)
    {
        string content = "BT /F1 18 Tf 30 150 Td (Encrypted PDF preview test) Tj ET\n";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream"
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (int offset in offsets.Skip(1)) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
