using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DecisionCouncil.App.Infrastructure;

/// <summary>
/// Small dark dialogs in the dib style (Session Deck's settings and city dialogs), in place of the white system MessageBox.
/// </summary>
public static class Dialogs
{
    private const string Caption = "Decision Council";

    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public static void Info(string message) => Show("DECISION COUNCIL", message, [("OK", MessageBoxResult.OK, true, true)]);

    public static void Error(string message) => Show("HEADS UP", message, [("OK", MessageBoxResult.OK, true, true)]);

    public static bool Confirm(string message) =>
        Show("PLEASE CONFIRM", message,
        [
            ("Cancel", MessageBoxResult.Cancel, false, true),
            ("Continue", MessageBoxResult.OK, true, false),
        ]) == MessageBoxResult.OK;

    /// <summary>Yes = save, No = discard, Cancel = stay.</summary>
    public static MessageBoxResult SaveDiscardCancel(string message) =>
        Show("UNSAVED CHANGES", message,
        [
            ("Cancel", MessageBoxResult.Cancel, false, true),
            ("Discard", MessageBoxResult.No, false, false),
            ("Save", MessageBoxResult.Yes, true, false),
        ]);

    private static MessageBoxResult Show(string heading, string message,
        IReadOnlyList<(string Label, MessageBoxResult Result, bool Primary, bool Cancel)> buttons)
    {
        var res = Application.Current.Resources;
        var owner = Owner;
        var window = new Window
        {
            Title = Caption,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner is null,
            Owner = owner,
            Background = (Brush)res["Bg.Dialog"],
            Foreground = (Brush)res["Text.Primary"],
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            UseLayoutRounding = true,
        };
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        WindowTheme.UseDarkTitleBar(window);

        var result = buttons.FirstOrDefault(b => b.Cancel).Result;
        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Children.Add(new TextBlock { Text = heading, Style = (Style)res["Heading"] });
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 });

        var bar = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        foreach (var b in buttons)
        {
            var button = new Button
            {
                Content = b.Label,
                IsDefault = b.Primary,
                IsCancel = b.Cancel,
                Style = b.Primary ? (Style)res["PrimaryButton"] : (Style)res[typeof(Button)],
            };
            var value = b.Result;
            button.Click += (_, _) => { result = value; window.Close(); };
            actions.Children.Add(button);
        }
        bar.Children.Add(actions);

        var credit = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        credit.Children.Add(new Path { Height = 10, Stretch = Stretch.Uniform, Fill = (Brush)res["Text.Label"], Data = (Geometry)res["DibLogo"], VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(credit);
        root.Children.Add(bar);

        window.Content = root;
        window.ShowDialog();
        return result;
    }
}
