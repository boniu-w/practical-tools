namespace PracticalTools.util;

public class MathUtil
{
    public static double ByteToKb(long bytes, int decimalPlaces = 3)
        => Math.Round((double)bytes / 1024, decimalPlaces);
}