using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DecisionCouncil.App.Controls;

/// <summary>Small entrance animations. All of them are skipped when Windows' "Show animations" is off.</summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Fades an element in while it slides up a few pixels.</summary>
    public static void Enter(UIElement element, double rise = 8, int milliseconds = 220)
    {
        if (!Enabled) return;
        var move = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = move;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, duration) { EasingFunction = ease });
    }
}

/// <summary>Shows pages with a short fade-and-rise each time the content changes.</summary>
public sealed class PageHost : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (oldContent is not null) Motion.Enter(this, rise: 10, milliseconds: 200);
    }
}

/// <summary>
/// A night sky of dim pixels that twinkle, like the backdrop of Session Deck's banner.
/// Positions come from a fixed seed, so the sky looks the same every time.
/// </summary>
public sealed class Starfield : FrameworkElement
{
    private static readonly Brush Star = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xCA, 0xCF)));
    private readonly (double X, double Y, double Size, double Phase)[] _stars;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public Starfield()
    {
        IsHitTestVisible = false;
        var random = new Random(7);
        _stars = Enumerable.Range(0, 70)
            .Select(_ => (random.NextDouble(), random.NextDouble() * 0.8, random.Next(4) == 0 ? 2.0 : 1.5, random.NextDouble() * 6))
            .ToArray();
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => { if (Motion.Enabled) _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = _clock.Elapsed.TotalSeconds;
        foreach (var (x, y, size, phase) in _stars)
        {
            var glow = Motion.Enabled ? 0.25 + 0.35 * (0.5 + 0.5 * Math.Sin(t * 1.3 + phase)) : 0.4;
            dc.PushOpacity(glow);
            dc.DrawRectangle(Star, null, new Rect(Math.Round(x * ActualWidth), Math.Round(y * ActualHeight), size, size));
            dc.Pop();
        }
    }

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
