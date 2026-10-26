using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DecisionCouncil.App.Controls;

/// <summary>Read-only, selectable Markdown display that grows with its content and lets the parent scroll.</summary>
public sealed class MarkdownView : RichTextBox
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView),
        new PropertyMetadata(null, (d, e) =>
        {
            var view = (MarkdownView)d;
            view.Refresh();
            // An answer arriving where there was none fades in, so new answers catch the eye
            if (string.IsNullOrEmpty(e.OldValue as string) && !string.IsNullOrEmpty(e.NewValue as string) && view.IsLoaded)
                Motion.Enter(view);
        }));

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownView()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xDB, 0xE0)); // dib body text
        SelectionBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = true;
        Cursor = Cursors.Arrow;
        Refresh();
    }

    private void Refresh() => Document = MarkdownRenderer.Render(Markdown, FontSize);

    // A RichTextBox eats the mouse wheel even when it cannot scroll; hand it to the enclosing ScrollViewer.
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this };
        (Parent as UIElement ?? VisualTreeHelper.GetParent(this) as UIElement)?.RaiseEvent(forwarded);
    }
}
