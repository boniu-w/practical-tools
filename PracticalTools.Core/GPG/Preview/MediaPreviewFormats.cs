namespace PracticalTools.GPG.Preview;

public static class MediaPreviewFormats
{
    public static bool Supports(string extension) => extension is ".mp4" or ".m4v" or ".mov" or ".m4a" or ".mkv" or ".webm" or ".avi" or ".wav" or ".mp3" or ".flac" or ".ogg" or ".ogv";
    public static string Validate(string name, ReadOnlySpan<byte> bytes)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        bool Starts(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) => data.StartsWith(signature);
        string? demux = extension switch
        {
            ".mp4" or ".m4v" or ".mov" or ".m4a" when bytes.Length >= 12 &&
                (bytes.Slice(4, 4).SequenceEqual("ftyp"u8) || bytes.Slice(4, 4).SequenceEqual("moov"u8)) => "mp4",
            ".mkv" or ".webm" when Starts(bytes, new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }) => "mkv",
            ".avi" when bytes.Length >= 12 && Starts(bytes, "RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("AVI "u8) => "avi",
            ".wav" when bytes.Length >= 12 && Starts(bytes, "RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8) => "wav",
            ".mp3" when Starts(bytes, "ID3"u8) || bytes.Length >= 2 && bytes[0] == 255 && (bytes[1] & 0xe0) == 0xe0 => "mpga",
            ".flac" when Starts(bytes, "fLaC"u8) => "flac",
            ".ogg" or ".ogv" when Starts(bytes, "OggS"u8) => "ogg",
            _ => null
        };
        return demux ?? throw new InvalidDataException("媒体文件头与支持的格式不匹配；不接受播放列表或网络地址。");
    }
}
