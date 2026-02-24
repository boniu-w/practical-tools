using System.Globalization;
using System.Windows.Data;

namespace PracticalTools.util;

public class SecondsToDurationConverter : IValueConverter
{
    private long MaxTimeSpanTicks = TimeSpan.MaxValue.Ticks;

    private long MinTimeSpanTicks = TimeSpan.MinValue.Ticks;

    // 1毫秒 = 10000 Ticks（转换常量）
    private const long TicksPerMillisecond = 10000L;
    private const long MillisPerSecond = 1000L;

    //public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    //{
    //    if (value is long seconds)
    //    {
    //        try
    //        {
    //            // 方法1：转为 "X天 X小时 X分 X秒"
    //            var ts = TimeSpan.FromSeconds(seconds);
    //            return $"{ts.Days}天 {ts.Hours}小时 {ts.Minutes}分 {ts.Seconds}秒";
    //        }
    //        catch (Exception e)
    //        {
    //            return "永久有效";
    //        }
    //    }

    //    return string.Empty;
    //}

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long seconds)
        {
            // 如果是 0 或负数，视为永久
            if (seconds <= 0)
                return "永久有效";

            // 超过 TimeSpan 最大值
            if (seconds > TimeSpan.MaxValue.TotalSeconds)
                return "永久有效";

            var ts = TimeSpan.FromSeconds(seconds);

            return $"{ts.Days}天 {ts.Hours}小时 {ts.Minutes}分 {ts.Seconds}秒";
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 判断指定的 Ticks 数是否在 TimeSpan 支持的范围内
    /// </summary>
    /// <param name="ticks">要校验的 Ticks 数（1 Tick = 100 纳秒）</param>
    /// <returns>true=在范围内，false=超出范围</returns>
    private bool IsTicksInTimeSpanRange(long ticks)
    {
        return ticks >= MinTimeSpanTicks && ticks <= MaxTimeSpanTicks;
    }

    /// <summary>
    /// 判断指定的毫秒数是否在 TimeSpan 支持的范围内
    /// </summary>
    /// <param name="milliseconds">要校验的毫秒数</param>
    /// <returns>true=在范围内，false=超出范围</returns>
    private bool IsMillisecondsInTimeSpanRange(long milliseconds)
    {
        if (milliseconds > 0)
        {
            return milliseconds <= MaxTimeSpanTicks / TicksPerMillisecond;
        }
        else if (milliseconds < 0)
        {
            return milliseconds >= MinTimeSpanTicks / TicksPerMillisecond;
        }
        else
        {
            return true;
        }
    }

    private bool IsSecondsInTimeSpanRange(long seconds)
    {
        if (seconds > 0)
        {
            return seconds <= MaxTimeSpanTicks / TicksPerMillisecond / MillisPerSecond;
        }
        else if (seconds < 0)
        {
            return seconds >= MinTimeSpanTicks / TicksPerMillisecond / MillisPerSecond;
        }
        else
        {
            return true;
        }
    }
}