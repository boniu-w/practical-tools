namespace PracticalTools.TestFixtures;

internal static class ImageFixture
{
    public static byte[] Avif() => Embedded("PracticalTools.TestFixtures.PreviewAvif.avif");

    public static byte[] AnimatedGif() => Embedded("PracticalTools.TestFixtures.PreviewAnimatedGif.gif");

    public static byte[] Bmp() => Convert.FromBase64String(
        "Qk26AAAAAAAAAIoAAAB8AAAABAAAAAQAAAABABgAAAAAADAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/AAD/AAD/AAAAAAAA/0JHUnOPwvUoUbgeFR6F6wEzMzMTZmZmJmZmZgaZmZkJPQrXAyhcjzIAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/AAD/");

    public static byte[] Gif() => Convert.FromBase64String(
        "R0lGODlhBAAEAPAAAP8AAAAAACH5BAAAAAAALAAAAAAEAAQAAAIEhI8JBQA7");

    public static byte[] Ico() => Convert.FromBase64String(
        "AAABAAEABAQCAAEAAQBQAAAAFgAAACgAAAAEAAAACAAAAAEAAQAAAAAAEAAAAAAAAAAAAAAAAgAAAAIAAAAAAP8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

    public static byte[] Webp() => Convert.FromBase64String(
        "UklGRjwAAABXRUJQVlA4IDAAAADQAQCdASoEAAQAAgA0JaACdLoB+AADsAD+8MQL/yC5YXXI1/8gP+QH/ID/+PIAAAA=");

    private static byte[] Embedded(string name)
    {
        using var stream = typeof(ImageFixture).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing fixture {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
