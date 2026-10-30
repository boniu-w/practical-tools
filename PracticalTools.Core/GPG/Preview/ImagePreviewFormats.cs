namespace PracticalTools.GPG.Preview;

public static class ImagePreviewFormats
{
    public static bool Supports(string extension) =>
        extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp" or ".avif";
}
