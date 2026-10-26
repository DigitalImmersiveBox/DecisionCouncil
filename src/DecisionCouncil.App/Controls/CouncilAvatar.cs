using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.App.ViewModels;

namespace DecisionCouncil.App.Controls;

/// <summary>
/// A council member as a small animated pixel creature, in Session Deck's mascot style and tinted with the agent's colour.
/// Its state drives the animation: idle bobbing and blinking while waiting, a "…" bubble and a shuffle while thinking,
/// a jump with happy eyes and a verdict flag when done, X eyes when failed, and a doze when cancelled or skipped.
/// All avatars share one clock and stand still when Windows animations are switched off.
/// </summary>
public sealed class CouncilAvatar : FrameworkElement
{
    public static readonly DependencyProperty AvatarProperty = Register(nameof(Avatar), typeof(string), null);
    public static readonly DependencyProperty AgentIdProperty = Register(nameof(AgentId), typeof(string), null);
    public static readonly DependencyProperty ColorProperty = Register(nameof(Color), typeof(string), "#6B707B");
    public static readonly DependencyProperty VerdictProperty = Register(nameof(Verdict), typeof(string), null);
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ColumnState), typeof(CouncilAvatar),
        new FrameworkPropertyMetadata(ColumnState.Waiting, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((CouncilAvatar)d)._changedAt = Now));
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(double), typeof(CouncilAvatar),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ShowExtrasProperty = DependencyProperty.Register(
        nameof(ShowExtras), typeof(bool), typeof(CouncilAvatar),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(CouncilAvatar),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Sprite key from the agent file (crab, robot, …); empty picks one from the id.</summary>
    public string? Avatar { get => (string?)GetValue(AvatarProperty); set => SetValue(AvatarProperty, value); }
    public string? AgentId { get => (string?)GetValue(AgentIdProperty); set => SetValue(AgentIdProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public string? Verdict { get => (string?)GetValue(VerdictProperty); set => SetValue(VerdictProperty, value); }
    public ColumnState State { get => (ColumnState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>One sprite pixel in DIPs: 3 on the stage, 2 next to names.</summary>
    public double PixelSize { get => (double)GetValue(PixelSizeProperty); set => SetValue(PixelSizeProperty, value); }
    /// <summary>Room for the thought bubble and the verdict flag; off for small inline avatars.</summary>
    public bool ShowExtras { get => (bool)GetValue(ShowExtrasProperty); set => SetValue(ShowExtrasProperty, value); }

    private const int MaxRows = 10;
    private double _changedAt = -10;

    public CouncilAvatar()
    {
        SnapsToDevicePixels = true;
        // Crisp pixel art: anti-aliasing leaves thin seams between neighbouring pixels
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Loaded += (_, _) => Clock.Join(this);
        Unloaded += (_, _) => Clock.Leave(this);
    }

    private double P => PixelSize;
    private double ExtraTop => ShowExtras ? 6 : 3;
    private double ExtraRight => ShowExtras ? 7 : 0;

    protected override Size MeasureOverride(Size availableSize) =>
        new((AvatarSprites.MaxColumns + ExtraRight) * P, (MaxRows + ExtraTop) * P);

    protected override void OnRender(DrawingContext dc)
    {
        var sprite = AvatarSprites.For(Avatar, AgentId);
        var (body, dark) = Inks.For(Color);
        var t = Now;
        var since = t - _changedAt;
        var phase = Phase(AgentId);
        var state = State;
        var animate = Clock.Animating;

        // Pose for this frame, in sprite pixels
        double lift = 0, jumpI = 0, look = 0, opacity = 1;
        var eyes = Eyes.Open;
        var tapping = false;
        bool Blink() => animate && (t + phase) % 3.7 < 0.13;
        double IdleBob() => animate && (t + phase) % 2.6 < 0.22 ? 1 : 0;

        switch (state)
        {
            case ColumnState.Running:
            case ColumnState.Retrying:
                lift = animate && ((t + phase) * 2.6) % 1 < 0.5 ? 1 : 0;
                tapping = animate && (int)((t + phase) * 4) % 2 == 0;
                look = animate && (t + phase) % 2.2 < 1.1 ? -0.5 : 0.5;
                break;
            case ColumnState.Done:
                if (animate && since < 0.7)
                {
                    var jump = Math.Sin(since / 0.7 * Math.PI) * 3;
                    if (sprite.JumpColumns is not null) jumpI = Math.Round(jump * 1.5); else lift = Math.Round(jump);
                }
                else lift = IdleBob();
                eyes = since < 2.2 ? Eyes.Happy : Blink() ? Eyes.Closed : Eyes.Open;
                break;
            case ColumnState.Failed:
                eyes = Eyes.Cross;
                opacity = 0.8;
                break;
            case ColumnState.Cancelled:
            case ColumnState.Skipped:
                eyes = Eyes.Closed;
                opacity = 0.45;
                break;
            default:
                lift = IdleBob();
                eyes = Blink() ? Eyes.Closed : Eyes.Open;
                break;
        }
        if (sprite.Floats) lift += 1 + (animate ? 0.6 * Math.Sin((t + phase) * 2.2) : 0);

        var ox = Math.Round((AvatarSprites.MaxColumns - sprite.Columns) / 2.0) * P;
        var height = (MaxRows + ExtraTop) * P;
        var y0 = height - sprite.Rows * P - lift * P;
        var spriteW = sprite.Columns * P;

        // Soft shadow on the floor, smaller while in the air
        var shadowW = Math.Max(2, sprite.Columns - 4 - lift) * P;
        dc.DrawRectangle(Inks.Shadow, null, new Rect(ox + (spriteW - shadowW) / 2, height - P * 0.5, shadowW, P * 0.5));

        dc.PushOpacity(opacity);
        for (var r = 0; r < sprite.Body.Length; r++)
        {
            for (var c = 0; c < sprite.Columns; c++)
            {
                var ink = sprite.Body[r][c] switch { 'B' => body, 'D' => dark, _ => null };
                if (ink is null) continue;
                var raise = jumpI > 0 && sprite.JumpColumns!.Contains(c) ? jumpI * P : 0;
                dc.DrawRectangle(ink, null, new Rect(ox + c * P, y0 + r * P - raise, P, P));
            }
        }

        var legTop = y0 + sprite.Body.Length * P;
        for (var i = 0; i < sprite.Legs.Length; i++)
        {
            var x = ox + sprite.Legs[i] * P;
            dc.DrawRectangle(body, null, new Rect(x, legTop, P, P));
            // Thinking: every other foot taps
            if (!(tapping && i % 2 == 0)) dc.DrawRectangle(body, null, new Rect(x, legTop + P, P, P));
        }

        var eyeInk = sprite.LightEyes ? Inks.LightEye : Inks.Eye;
        foreach (var (col, row) in sprite.Eyes)
        {
            var ex = ox + (col + look) * P;
            var ey = y0 + row * P;
            switch (eyes)
            {
                case Eyes.Happy:
                    dc.DrawRectangle(eyeInk, null, new Rect(ex - P / 2, ey + P / 2, P / 2, P / 2));
                    dc.DrawRectangle(eyeInk, null, new Rect(ex, ey, P, P / 2));
                    dc.DrawRectangle(eyeInk, null, new Rect(ex + P, ey + P / 2, P / 2, P / 2));
                    var blushX = col < sprite.Columns / 2.0 ? ex - 1.5 * P : ex + 1.5 * P;
                    dc.DrawRectangle(Inks.Blush, null, new Rect(blushX, ey + 1.5 * P, P, P / 2));
                    break;
                case Eyes.Closed:
                    dc.DrawRectangle(eyeInk, null, new Rect(ex, ey + 1.25 * P, P, P / 2));
                    break;
                case Eyes.Cross:
                    foreach (var (dx, dy) in new[] { (-0.5, 0.0), (1.0, 0.0), (0.25, 0.75), (-0.5, 1.5), (1.0, 1.5) })
                        dc.DrawRectangle(eyeInk, null, new Rect(ex + dx * P, ey + dy * P, P / 2, P / 2));
                    break;
                default:
                    dc.DrawRectangle(eyeInk, null, new Rect(ex, ey, P, 2 * P));
                    break;
            }
        }
        dc.Pop();

        if (!ShowExtras) return;
        var right = ox + spriteW;

        // Thinking: a cloud with dots that fill up; retrying: an amber cloud with "!"
        if (state is ColumnState.Running or ColumnState.Retrying)
        {
            var bx = right - P;
            var by = y0 - 5.5 * P;
            var cloud = state == ColumnState.Retrying ? Palette.Check : Inks.Cloud;
            dc.DrawRectangle(cloud, null, new Rect(bx + P, by, 5 * P, P));
            dc.DrawRectangle(cloud, null, new Rect(bx, by + P, 7 * P, 2 * P));
            dc.DrawRectangle(cloud, null, new Rect(bx + P, by + 3 * P, 5 * P, P));
            dc.DrawRectangle(cloud, null, new Rect(bx, by + 4.5 * P, P, P));
            if (state == ColumnState.Retrying)
            {
                dc.DrawRectangle(Inks.Eye, null, new Rect(bx + 3 * P, by + 0.5 * P, P, 1.6 * P));
                dc.DrawRectangle(Inks.Eye, null, new Rect(bx + 3 * P, by + 2.6 * P, P, 0.6 * P));
            }
            else
            {
                var dots = animate ? (int)((t * 2.5 + phase) % 4) : 3;
                for (var d = 0; d < dots; d++)
                    dc.DrawRectangle(Inks.Eye, null, new Rect(bx + (1 + 2 * d) * P, by + 1.5 * P, P, P));
            }
        }

        // Dozing: a z that drifts up and fades
        if (state is ColumnState.Cancelled or ColumnState.Skipped && animate)
        {
            var k = (t + phase) % 2.4 / 2.4;
            var zp = P * 0.8;
            var zx = right + P;
            var zy = y0 - k * 4 * P;
            dc.PushOpacity(1 - k);
            dc.DrawRectangle(Inks.Zed, null, new Rect(zx, zy, 3 * zp, zp));
            dc.DrawRectangle(Inks.Zed, null, new Rect(zx + zp, zy + zp, zp, zp));
            dc.DrawRectangle(Inks.Zed, null, new Rect(zx, zy + 2 * zp, 3 * zp, zp));
            dc.Pop();
        }

        // Done: hold up a flag in the verdict's colour, waving a little
        if (state == ColumnState.Done && Verdict is { Length: > 0 } verdict)
        {
            var flagInk = verdict switch
            {
                "Go" => Palette.Good,
                "Go with changes" => Palette.Working,
                "Not yet" => Palette.Check,
                "No-go" => Palette.NeedsYou,
                _ => Palette.Done,
            };
            var sx = right + 2 * P;
            var top = y0 - 3 * P;
            dc.DrawRectangle(Inks.Stick, null, new Rect(sx, top, P * 0.75, (sprite.Body.Length + 3) * P));
            var wave = animate && (t * 3 + phase) % 1 < 0.5 ? P / 2 : 0;
            dc.DrawRectangle(flagInk, null, new Rect(sx + P * 0.75, top, 4 * P, 1.5 * P + wave));
            dc.DrawRectangle(flagInk, null, new Rect(sx + P * 0.75, top + 1.5 * P + wave, 4 * P - wave, 1.5 * P - wave));
        }

        // Sparks around the jump
        if (state == ColumnState.Done && animate && since < 0.9)
        {
            dc.PushOpacity(1 - since / 0.9);
            var rise = since * 4 * P;
            dc.DrawRectangle(Palette.Check, null, new Rect(ox - P, y0 - 2 * P - rise / 2, P, P));
            dc.DrawRectangle(Palette.Check, null, new Rect(right, y0 - 3 * P - rise, P, P));
            dc.DrawRectangle(Palette.Check, null, new Rect(ox + spriteW / 2, y0 - 5 * P - rise, P, P));
            dc.Pop();
        }
    }

    private enum Eyes { Open, Closed, Happy, Cross }

    private static double Phase(string? id)
    {
        var h = 0;
        foreach (var c in id ?? "") h = h * 31 + c;
        return Math.Abs(h % 1000) / 137.0;
    }

    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static double Now => Watch.Elapsed.TotalSeconds;

    /// <summary>One timer for every visible avatar; nothing ticks when none is shown.</summary>
    private static class Clock
    {
        private static readonly HashSet<CouncilAvatar> Live = [];
        private static DispatcherTimer? _timer;

        /// <summary>Follows the Windows setting "Show animations"; off means still pictures.</summary>
        public static bool Animating => SystemParameters.ClientAreaAnimation;

        public static void Join(CouncilAvatar a)
        {
            Live.Add(a);
            if (_timer is not null || !Animating) return;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(80) };
            _timer.Tick += (_, _) =>
            {
                foreach (var avatar in Live)
                    if (avatar.IsVisible) avatar.InvalidateVisual();
            };
            _timer.Start();
        }

        public static void Leave(CouncilAvatar a)
        {
            Live.Remove(a);
            if (Live.Count > 0 || _timer is null) return;
            _timer.Stop();
            _timer = null;
        }
    }

    /// <summary>Frozen brushes; agent colours are cached with their darker shade.</summary>
    private static class Inks
    {
        public static readonly Brush Eye = Make(System.Windows.Media.Color.FromRgb(0x16, 0x16, 0x18));
        public static readonly Brush LightEye = Make(System.Windows.Media.Color.FromRgb(0xE6, 0xE7, 0xEA));
        public static readonly Brush Blush = Make(System.Windows.Media.Color.FromRgb(0xF0, 0xA0, 0x8C));
        public static readonly Brush Cloud = Make(System.Windows.Media.Color.FromRgb(0xE6, 0xE7, 0xEA));
        public static readonly Brush Zed = Make(System.Windows.Media.Color.FromRgb(0x7D, 0x80, 0x8A));
        public static readonly Brush Stick = Make(System.Windows.Media.Color.FromRgb(0x8A, 0x8D, 0x96));
        public static readonly Brush Shadow = Make(System.Windows.Media.Color.FromArgb(0x55, 0x00, 0x00, 0x00));

        private static readonly Dictionary<string, (Brush Body, Brush Dark)> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static (Brush Body, Brush Dark) For(string? hex)
        {
            hex ??= "#6B707B";
            if (Cache.TryGetValue(hex, out var pair)) return pair;
            System.Windows.Media.Color c;
            try { c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex); }
            catch (FormatException) { c = System.Windows.Media.Color.FromRgb(0x6B, 0x70, 0x7B); }
            var dark = System.Windows.Media.Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62));
            return Cache[hex] = (Make(c), Make(dark));
        }

        private static Brush Make(System.Windows.Media.Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
