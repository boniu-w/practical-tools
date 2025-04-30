namespace PracticalTools.util;

public class ColumnHeaderAttribute : Attribute
{
    public string DisplayName  { get; set; }

    public ColumnHeaderAttribute(string displayName)
    {
        DisplayName  = displayName;
    }
}