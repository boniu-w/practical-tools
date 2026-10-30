using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using PracticalTools.Desktop.Preview;
using PracticalTools.TestFixtures;
using Xunit;

namespace PracticalTools.Desktop.Tests;

public class MediaPlaybackTests
{
    private static async Task Until(Func<bool> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTime.UtcNow > end) throw new TimeoutException("VLC state did not change."); await Task.Delay(30); }
    }
    [Fact]
    public async Task NativeAudioPlaybackPauseSeekStopAndReplayUseOwnedMemory()
    {
        var bytes = MediaFixture.Wave(); using var session = new MediaPlayback(bytes, "wav", silent: true);
        Array.Clear(bytes); session.Play();
        await Until(() => session.Player.Time > 100 && session.Player.IsSeekable);
        session.Pause(); await Until(() => session.Player.State == VLCState.Paused);
        session.Seek(.5f); Assert.InRange(session.Player.Position, .45f, .55f);
        session.Player.Volume = 23; // Dummy audio output does not expose a device volume value.
        session.Stop(); Assert.Equal(VLCState.Stopped, session.Player.State);
        session.Play(); await Until(() => session.Player.State == VLCState.Playing);
    }
    [Fact]
    public async Task NativeVideoDecodesFramesFromMemory()
    {
        using var session = new MediaPlayback(MediaFixture.Avi(), "avi", silent: true);
        var buffer = Marshal.AllocHGlobal(16); int frames = 0;
        try
        {
            session.Player.SetVideoFormat("RV32", 2, 2, 8);
            session.Player.SetVideoCallbacks((_, planes) => { Marshal.WriteIntPtr(planes, buffer); return IntPtr.Zero; }, null,
                (_, _) => Interlocked.Increment(ref frames));
            session.Play(); await Until(() => Volatile.Read(ref frames) > 0);
            session.Stop(); Assert.True(frames > 0);
        }
        finally { session.Stop(); Marshal.FreeHGlobal(buffer); }
    }
    [Fact]
    public async Task NativeAacPlaysFromMemory()
    {
        using var session = new MediaPlayback(MediaFixture.Aac(), "aac", silent: true);
        session.Play();
        await Until(() => session.Player.Time > 100);
        session.Stop();
        Assert.Equal(VLCState.Stopped, session.Player.State);
    }

    [Fact]
    public async Task NativeWmvDecodesFramesFromMemory()
    {
        using var session = new MediaPlayback(MediaFixture.Wmv(), "asf", silent: true);
        var buffer = Marshal.AllocHGlobal(1920 * 1080 * 4); int frames = 0;
        try
        {
            session.Player.SetVideoCallbacks((_, planes) => { Marshal.WriteIntPtr(planes, buffer); return IntPtr.Zero; }, null,
                (_, _) => Interlocked.Increment(ref frames));
            session.Play(); await Until(() => Volatile.Read(ref frames) > 0);
            session.Stop(); Assert.True(frames > 0);
        }
        finally { session.Stop(); Marshal.FreeHGlobal(buffer); }
    }

    [Fact]
    public async Task MalformedMediaReportsErrorAndDisposalIsRepeatable()
    {
        var session = new MediaPlayback(new byte[] { 1, 2, 3 }, "wav", silent: true);
        try { session.Play(); await Until(() => session.Player.State is VLCState.Error or VLCState.Ended); }
        finally { session.Dispose(); session.Dispose(); }
        Assert.Throws<ObjectDisposedException>(() => session.Play());
    }
}
