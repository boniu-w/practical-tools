using LibVLCSharp.Shared;

namespace PracticalTools.Desktop.Preview;

/// <summary>Owns the input buffer until VLC has stopped all stream callbacks.</summary>
public sealed class MediaPlayback : IDisposable
{
    private LibVLC? _engine;
    private Media? _media;
    private StreamMediaInput? _input;
    private MemoryStream? _stream;
    private byte[]? _bytes;
    private MediaPlayer? _player;
    public MediaPlayer Player => _player ?? throw new ObjectDisposedException(nameof(MediaPlayback));
    public MediaPlayback(byte[] bytes, string demux, bool silent = false)
    {
        if (demux is not ("mp4" or "mkv" or "avi" or "wav" or "mpga" or "flac" or "ogg")) throw new ArgumentException("不支持的媒体格式。");
        if (bytes.Length == 0 || bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("媒体超过预览大小限制。");
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            var options = new List<string> { "--ignore-config", "--no-video-title-show", "--no-sub-autodetect-file", "--no-lua", "--no-metadata-network-access", "--no-media-library", "--no-osd", "--avcodec-hw=none" };
            if (silent) { options.Add("--aout=dummy"); options.Add("--vout=dummy"); }
            _engine = new LibVLC(options.ToArray());
            _bytes = bytes.ToArray(); _stream = new MemoryStream(_bytes, writable: false);
            _input = new StreamMediaInput(_stream);
            _media = new Media(_engine, _input);
            _media.AddOption(":demux=" + demux);
            _player = new MediaPlayer(_media); _player.Volume = 50;
        }
        catch { Dispose(); throw; }
    }
    public void Play()
    {
        if (Player.State == VLCState.Ended) Player.Stop();
        if (!Player.Play()) throw new InvalidOperationException("播放器无法开始播放此媒体。");
    }
    public void Pause() => Player.SetPause(true);
    public void Stop() => Player.Stop();
    public void Seek(float position)
    {
        if (!Player.IsSeekable) throw new InvalidOperationException("当前媒体尚不能跳转，请先开始播放。");
        Player.Position = Math.Clamp(position, 0, 1);
    }
    public void Dispose()
    {
        // Never invoked from a VLC event callback. Stop before releasing the stream.
        _player?.Stop(); _player?.Dispose(); _player = null;
        _media?.Dispose(); _media = null;
        _input?.Dispose(); _input = null;
        _stream?.Dispose(); _stream = null;
        if (_bytes != null) Array.Clear(_bytes); _bytes = null;
        _engine?.Dispose(); _engine = null;
    }
}
