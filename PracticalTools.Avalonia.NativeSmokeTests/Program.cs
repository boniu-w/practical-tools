using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using PracticalTools.Desktop.Preview;
using PracticalTools.Desktop.Views;
using PracticalTools.TestFixtures;

namespace PracticalTools.NativeSmokeTests;

internal static class Program
{
    public static bool VisualMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("This regression requires a Windows desktop session."); return 2; }
        VisualMode = args.Contains("--visual");
        try { return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

public sealed class SmokeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        bool visual = Program.VisualMode;
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Position = visual ? new PixelPoint(100, 100) : new PixelPoint(-30000, -30000),
            ShowInTaskbar = visual,
            ShowActivated = visual,
            Topmost = visual,
            Title = "Native preview regression" + (visual ? " (visual)" : "")
        };
        lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                if (visual) await RunVisualAsync(window);
                else await RunAutomatedAsync(window);
                lifetime.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                lifetime.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunAutomatedAsync(Window window)
    {
        var cases = new (string Name, byte[] Bytes, string Demux, bool Video)[]
        {
            ("AVI", MediaFixture.Avi(), "avi", true),
            ("WMV", MediaFixture.Wmv(), "asf", true),
            ("WAV", MediaFixture.Wave(), "wav", false),
            ("AAC", MediaFixture.Aac(), "aac", false)
        };
        foreach (var item in cases)
        {
            for (int cycle = 0; cycle < 2; cycle++)
            {
                MediaPlayback? playback = null;
                var view = new MediaPreviewView { CreatePlayback = (bytes, demux) => playback = new(bytes, demux) };
                window.Content = view;
                view.Open(item.Bytes, item.Demux, item.Video);
                window.UpdateLayout();
                if (item.Video && playback!.Player.Hwnd == IntPtr.Zero)
                    throw new InvalidOperationException($"{item.Name}: VideoView did not attach a native HWND.");
                playback!.Player.Mute = true;
                playback.Play();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (playback.Player.Time < 100)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException($"{item.Name}: playback did not advance.");
                    await Task.Delay(50);
                }
                window.Content = null;
                window.UpdateLayout();
                try
                {
                    playback.Play();
                    throw new InvalidOperationException($"{item.Name}: detached player was not disposed.");
                }
                catch (ObjectDisposedException) { }
            }
            Console.WriteLine($"PASS cycle: {item.Name}");
        }
        Console.WriteLine("PASS: native HWND creation, AVI/WMV video and WAV/AAC audio playback, detach and reopen.");
    }

    private static async Task RunVisualAsync(Window window)
    {
        Console.WriteLine("Visual mode: video, GIF animation and AVIF are shown for a few seconds each.");

        async Task ShowMediaAsync(string name, byte[] bytes, string demux, bool video, int seconds)
        {
            MediaPlayback? playback = null;
            var view = new MediaPreviewView { CreatePlayback = (data, forcedDemux) => playback = new(data, forcedDemux) };
            window.Content = view;
            view.Open(bytes, demux, video);
            window.UpdateLayout();
            if (video && playback!.Player.Hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"{name}: VideoView did not attach a native HWND.");
            playback!.Player.Mute = video;
            playback.Player.Volume = video ? 0 : 30;
            playback.Play();
            Console.WriteLine($"VISUAL: {name}");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            window.Content = null;
            window.UpdateLayout();
        }

        var gif = PreviewImageLoader.Load(ImageFixture.AnimatedGif(), ".gif");
        window.Content = new GifImage { Source = gif.GifStream!, Stretch = Stretch.Uniform, Margin = new Thickness(20) };
        window.UpdateLayout();
        Console.WriteLine("VISUAL: animated GIF");
        await Task.Delay(TimeSpan.FromSeconds(8));
        window.Content = null;

        var avif = PreviewImageLoader.Load(ImageFixture.Avif(), ".avif");
        window.Content = new Image { Source = avif.Bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(20) };
        window.UpdateLayout();
        Console.WriteLine("VISUAL: AVIF");
        await Task.Delay(TimeSpan.FromSeconds(4));
        window.Content = null;

        await ShowMediaAsync("AVI", MediaFixture.Avi(), "avi", true, 4);
        await ShowMediaAsync("WMV", MediaFixture.Wmv(), "asf", true, 4);
        await ShowMediaAsync("WAV", MediaFixture.Wave(), "wav", false, 3);
        await ShowMediaAsync("AAC", MediaFixture.Aac(), "aac", false, 3);

        gif.GifStream?.Dispose();
        avif.Bitmap?.Dispose();
        Console.WriteLine("PASS: visual sequence completed.");
    }
}
