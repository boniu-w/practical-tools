namespace TestProject1;

[TestClass]
public class DateTest
{
    public void DateTest1()
    {
        long milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Console.WriteLine(milliseconds);
        DateTimeOffset dateTimeOffset = DateTimeOffset.Now;
        Console.WriteLine(dateTimeOffset.ToString());

        DateTime dateTime = DateTime.Now;
        Console.WriteLine(dateTime.ToString());
    }
}