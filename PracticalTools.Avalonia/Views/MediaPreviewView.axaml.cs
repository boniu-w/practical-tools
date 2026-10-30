using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using PracticalTools.Desktop.Preview;

namespace PracticalTools.Desktop.Views;

public partial class MediaPreviewView : UserControl
{
    private MediaPlayback? _session;
    private VideoView? _video;
    private readonly DispatcherTimer _timer;
    public Func<byte[], string, MediaPlayback> CreatePlayback { get; set; } = (bytes, demux) => new(bytes, demux);
    public MediaPreviewView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Update();
        DetachedFromVisualTree += (_, _) => Close();
        Volume.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && _session != null) _session.Player.Volume = (int)Volume.Value; };
    }
    public void Open(byte[] bytes, string demux, bool video)
    {
        Close();
        try
        {
            _session = CreatePlayback(bytes, demux);
            _session.Player.Volume = (int)Volume.Value;
            if (video) { _video = new VideoView { MediaPlayer = _session.Player }; VideoHost.Content = _video; }
            else VideoHost.Content = new TextBlock { Text = "音频预览", FontSize = 24 };
            SeekPosition.Value = PlaybackProgress.Value = 0;
            PlaybackStatus.Text = "媒体已就绪，点击播放。";
            _timer.Start();
        }
        catch { Close(); throw; }
    }
    public void Close()
    {
        _timer.Stop();
        // Stop while the native video host still exists, then detach before disposing the player.
        _session?.Stop();
        if (_video != null) _video.MediaPlayer = null;
        VideoHost.Content = null; _video = null;
        _session?.Dispose(); _session = null;
        PlaybackProgress.Value = SeekPosition.Value = 0; PlaybackStatus.Text = null;
        SeekButton.IsEnabled = false;
    }
    private void Update()
    {
        if (_session == null) return;
        var player = _session.Player;
        if (player.State == VLCState.Error) { Close(); PlaybackStatus.Text = "无法解码此媒体，可能已损坏或编码不受支持。"; return; }
        SeekButton.IsEnabled = player.IsSeekable;
        PlaybackProgress.Value = Math.Max(0, player.Position * 100);
        string state = player.State switch { VLCState.Playing => "播放中", VLCState.Paused => "已暂停", VLCState.Ended => "播放结束", VLCState.Stopped => "已停止", _ => "等待播放" };
        PlaybackStatus.Text = $"{state}  {TimeSpan.FromMilliseconds(Math.Max(0, player.Time)):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(Math.Max(0, player.Length)):hh\\:mm\\:ss}";
    }
    private void Act(Action<MediaPlayback> action)
    {
        if (_session == null) return;
        try { action(_session); Update(); }
        catch (Exception ex) { PlaybackStatus.Text = ex.Message; }
    }
    private void Play(object? sender, RoutedEventArgs e) => Act(s => s.Play());
    private void Pause(object? sender, RoutedEventArgs e) => Act(s => s.Pause());
    private void Stop(object? sender, RoutedEventArgs e) => Act(s => s.Stop());
    private void Seek(object? sender, RoutedEventArgs e) => Act(s => s.Seek((float)(SeekPosition.Value / 100)));
}
