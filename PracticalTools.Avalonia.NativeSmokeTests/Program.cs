using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using PracticalTools.Desktop.Preview;
using PracticalTools.Desktop.Views;
using PracticalTools.TestFixtures;

namespace PracticalTools.NativeSmokeTests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("This regression requires a Windows desktop session."); return 2; }
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
        var window = new Window { Width = 640, Height = 480, Position = new PixelPoint(-30000, -30000),
            ShowInTaskbar = false, ShowActivated = false, Title = "Native video regression" };
        lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            MediaPreviewView? view = null;
            try
            {
                // Real Win32 layout/NativeControlHost, not Avalonia.Headless or dummy video output.
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    MediaPlayback? playback = null;
                    view = new MediaPreviewView { CreatePlayback = (bytes, demux) => playback = new(bytes, demux) };
                    window.Content = view;
                    view.Open(MediaFixture.Avi(), "avi", video: true);
                    window.UpdateLayout();
                    if (playback!.Player.Hwnd == IntPtr.Zero) throw new InvalidOperationException("VideoView did not attach a native HWND.");
                    playback.Player.Mute = true;
                    playback.Play();
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (playback.Player.Time < 100)
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("Native video playback did not advance.");
                        await Task.Delay(50);
                    }
                    window.Content = null; window.UpdateLayout();
                    try { playback.Play(); throw new InvalidOperationException("Detached player was not disposed."); }
                    catch (ObjectDisposedException) { }
                    view = null;
                }
                Console.WriteLine("PASS: native HWND creation, video playback, detach and reopen (2 cycles).");
                lifetime.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex); view?.Close(); lifetime.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
