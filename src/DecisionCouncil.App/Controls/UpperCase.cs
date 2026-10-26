using System.Globalization;
using System.Windows.Data;

namespace DecisionCouncil.App.Controls;

/// <summary>Turns a bound name into an uppercase section label ("Shared prompts" → "SHARED PROMPTS").</summary>
public sealed class UpperCase : IValueConverter
{
    public static readonly UpperCase Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
