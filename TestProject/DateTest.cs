namespace TestProject1;

[TestClass]
public class DateTest
{
    private long MaxTimeSpanTicks = TimeSpan.MaxValue.Ticks;

    private long MinTimeSpanTicks = TimeSpan.MinValue.Ticks;

    // 1毫秒 = 10000 Ticks（转换常量）
    private const long TicksPerMillisecond = 10000L;
    private const long MillisPerSecond = 1000L;

    // [TestMethod]
    public void DateTest1()
    {
        long milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Console.WriteLine(milliseconds);
        DateTimeOffset dateTimeOffset = DateTimeOffset.Now;
        Console.WriteLine(dateTimeOffset.ToString());

        DateTime dateTime = DateTime.Now;
        Console.WriteLine(dateTime.ToString());
    }

    [TestMethod]
    public void SecondsTest()
    {
        long seconds = long.MaxValue;
        if (!IsSecondsInTimeSpanRange(seconds) && seconds >= int.MaxValue)
        {
            seconds = MaxTimeSpanTicks / TicksPerMillisecond / MillisPerSecond;
        }
        
        // 方法2：只显示 "HH:mm:ss"（适合 <= 24 小时）
        // return ts.ToString(@"hh\:mm\:ss");

        // 方法3：显示总小时和分钟，如 "125小时30分"
        // return $"{(int)ts.TotalHours}小时 {ts.Minutes}分";
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