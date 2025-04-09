using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PracticalTools.util;

public class DateTimeConverter
{
    public static ValueConverter<DateTime?, string> Converter => new ValueConverter<DateTime?, string>(
        v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
        v => v != null ? DateTime.Parse(v) : null);
}