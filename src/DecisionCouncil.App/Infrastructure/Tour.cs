using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DecisionCouncil.App.ViewModels;
using DecisionCouncil.Core.Settings;

namespace DecisionCouncil.App.Infrastructure;

/// <summary>
/// <c>--tour &lt;folder&gt;</c>: clicks through every screen with the demo provider and saves a PNG of each.
/// A visual smoke test; use it with <c>--data</c> pointing at a throwaway folder.
/// </summary>
public static class Tour
{
    public static async Task RunAsync(Window window, MainViewModel vm, string outDir)
    {
        Directory.CreateDirectory(outDir);
        vm.Council.Settings.Provider = ProviderKind.Demo; // in memory only, never saved
        vm.Council.Settings.DemoSpeed = 2.5;

        vm.GoTo("live");
        await Shot(window, outDir, "00a-live-empty");
        vm.GoTo("journal");
        await Shot(window, outDir, "00b-journal-empty");
        vm.GoTo("new");
        await Shot(window, outDir, "01-new-idea-empty");
        vm.NewIdea.LoadSampleCommand.Execute(null);
        await Shot(window, outDir, "02-new-idea-sample");

        vm.NewIdea.StartCommand.Execute(null);
        await Task.Delay(1800);
        await Shot(window, outDir, "03-live-round1-running");
        while (vm.Live is { IsRunning: true, SelectedTab: 0 }) await Task.Delay(100);
        await Task.Delay(700);
        await Shot(window, outDir, "03b-live-round2-running");
        while (vm.Live is { IsRunning: true }) await Task.Delay(200);
        await Task.Delay(300);
        await Shot(window, outDir, "04-live-synthesis-done");
        vm.Live!.SelectedTab = 1;
        await Shot(window, outDir, "05-live-round2");

        vm.Live.OpenResultCommand.Execute(null);
        await Shot(window, outDir, "06-result");
        if (vm.CurrentPage is ResultViewModel result)
        {
            result.OpenDecisionFormCommand.Execute(null);
            result.Choice = "Go with changes";
            result.Reason = "Pilot with one training centre first; no certification claims.";
            await Shot(window, outDir, "07-result-decision-form");
            result.SaveDecisionCommand.Execute(null);
            await Shot(window, outDir, "08-result-decided");
        }

        foreach (var (key, name) in new[] { ("journal", "09-journal"), ("agents", "10-agents"), ("context", "11-context"), ("settings", "12-settings") })
        {
            vm.GoTo(key);
            await Shot(window, outDir, name);
        }

        // Second debate: a generic idea with the Quick check preset, cancelled mid-way.
        vm.GoTo("new");
        vm.NewIdea.ClearCommand.Execute(null);
        vm.NewIdea.IdeaText = "Offer a paid monthly newsletter on VR training trends for HR managers.";
        vm.NewIdea.ApplyPresetCommand.Execute(vm.NewIdea.Presets.First());
        await Shot(window, outDir, "13-new-idea-quick-check");
        vm.NewIdea.StartCommand.Execute(null);
        await Task.Delay(1500);
        vm.Live!.Cancel();
        while (vm.Live is { IsRunning: true }) await Task.Delay(100);
        await Shot(window, outDir, "14-live-cancelled");
        vm.GoTo("journal");
        await Shot(window, outDir, "15-journal-two-debates");

        // A dark dialog: capture it while it is open, then dismiss it.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w != window) is { } dialog)
            {
                await Shot(dialog, outDir, "16-dialog");
                dialog.Close();
            }
        };
        timer.Start();
        Dialogs.Confirm("Switch to the Claude Code CLI? From now on, debates and test runs use your Claude plan's credit for programmatic use.");

        window.Close();
    }

    private static async Task Shot(Window window, string outDir, string name)
    {
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Paint the window background first; the content alone would be transparent where the window shows through.
        var area = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        var frame = new DrawingVisual();
        using (var dc = frame.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, area);
            // Absolute viewbox: scrolled-away content would otherwise widen the brush and squeeze the picture
            dc.DrawRectangle(new VisualBrush(content) { Viewbox = area, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None }, null, area);
        }
        bmp.Render(frame);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        await using var fs = File.Create(Path.Combine(outDir, name + ".png"));
        encoder.Save(fs);
    }
}
