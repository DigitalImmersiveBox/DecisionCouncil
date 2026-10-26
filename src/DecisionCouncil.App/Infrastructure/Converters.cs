using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DecisionCouncil.App.ViewModels;

namespace DecisionCouncil.App.Infrastructure;

/// <summary>true → Visible. Pass "invert" as parameter for the opposite. Also treats null/empty strings as false.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "invert") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is string s && ColorConverter.ConvertFromString(s) is Color c)
            {
                if (parameter as string == "soft") c = Color.FromArgb(0x22, c.R, c.G, c.B);
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException) { }
        return Palette.Done;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The dib status colours, for code that cannot use the theme's StaticResources.</summary>
public static class Palette
{
    public static readonly SolidColorBrush NeedsYou = Make(0xFF, 0x7A, 0x59);
    public static readonly SolidColorBrush Check = Make(0xF5, 0xB8, 0x3D);
    public static readonly SolidColorBrush Good = Make(0x4A, 0xDE, 0x80);
    public static readonly SolidColorBrush Working = Make(0x60, 0xA5, 0xFA);
    public static readonly SolidColorBrush Done = Make(0x6B, 0x70, 0x7B);
    public static readonly SolidColorBrush Idle = Make(0x4A, 0x4E, 0x57);

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Verdict text → chip colour: green go, blue go with changes, amber not yet, orange no-go.</summary>
public sealed class VerdictBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "Go" => Palette.Good,
        "Go with changes" => Palette.Working,
        "Not yet" => Palette.Check,
        "No-go" => Palette.NeedsYou,
        _ => Palette.Done,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Agent column state → status colour, same meanings as Session Deck's session states.</summary>
public sealed class ColumnStateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ColumnState.Running => Palette.Working,
        ColumnState.Retrying => Palette.Check,
        ColumnState.Done => Palette.Good,
        ColumnState.Failed => Palette.NeedsYou,
        ColumnState.Waiting => Palette.Idle,
        _ => Palette.Done,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
