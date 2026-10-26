using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DecisionCouncil.App.Controls;

/// <summary>
/// Small Markdown to FlowDocument converter for agent answers: headings, paragraphs, bullet and numbered lists,
/// quotes, rules, and inline bold, italic and code. Enough for the fixed output formats, not a full CommonMark parser.
/// </summary>
public static partial class MarkdownRenderer
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*[-*•]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*(\d+)[.)]\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"(\*\*.+?\*\*|`[^`]+`|(?<![\w*])\*(?!\s)[^*]+?\*(?![\w*]))")]
    private static partial Regex Inline();

    // dib dark palette: headings and bold a step brighter than body text
    private static readonly Brush Strong = Frozen(0xE6, 0xE7, 0xEA);
    private static readonly Brush QuoteLine = Frozen(0x3A, 0x3D, 0x45);
    private static readonly Brush CodeBack = Frozen(0x26, 0x29, 0x30);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static FlowDocument Render(string? markdown, double fontSize = 13)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            LineHeight = fontSize * 1.45,
            TextAlignment = TextAlignment.Left,
        };
        if (string.IsNullOrWhiteSpace(markdown)) return doc;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        List? list = null;
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            AddInlines(p.Inlines, string.Join(" ", paragraph));
            doc.Blocks.Add(p);
            paragraph.Clear();
        }

        void FlushList()
        {
            if (list is null) return;
            doc.Blocks.Add(list);
            list = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (Heading().Match(line) is { Success: true } h)
            {
                FlushParagraph();
                FlushList();
                var level = h.Groups[1].Value.Length;
                var p = new Paragraph
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Strong,
                    FontSize = level switch { 1 => fontSize + 6, 2 => fontSize + 3, 3 => fontSize + 1, _ => fontSize },
                    Margin = new Thickness(0, doc.Blocks.Count == 0 ? 0 : 10, 0, 4),
                };
                AddInlines(p.Inlines, h.Groups[2].Value);
                doc.Blocks.Add(p);
                continue;
            }

            if (line.Trim() is "---" or "***" or "___")
            {
                FlushParagraph();
                FlushList();
                doc.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Separator { Margin = new Thickness(0, 6, 0, 6) }));
                continue;
            }

            var bullet = Bullet().Match(line);
            var numbered = Numbered().Match(line);
            if (bullet.Success || numbered.Success)
            {
                FlushParagraph();
                var marker = bullet.Success ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal;
                if (list is null || list.MarkerStyle != marker)
                {
                    FlushList();
                    list = new List
                    {
                        MarkerStyle = marker,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(18, 0, 0, 0),
                    };
                    if (numbered.Success && int.TryParse(numbered.Groups[1].Value, out var start)) list.StartIndex = start;
                }
                var item = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                AddInlines(item.Inlines, bullet.Success ? bullet.Groups[1].Value : numbered.Groups[2].Value);
                list.ListItems.Add(new ListItem(item));
                continue;
            }

            if (line.StartsWith('>'))
            {
                FlushParagraph();
                FlushList();
                var q = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 2, 0, 2),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = QuoteLine,
                    FontStyle = FontStyles.Italic,
                };
                AddInlines(q.Inlines, line.TrimStart('>', ' '));
                doc.Blocks.Add(q);
                continue;
            }

            // A plain line right after a list item continues that item (wrapped text); otherwise it is paragraph text.
            if (list is not null && raw.StartsWith("  ") && list.ListItems.LastListItem?.Blocks.LastBlock is Paragraph last)
            {
                last.Inlines.Add(new Run(" "));
                AddInlines(last.Inlines, line.Trim());
                continue;
            }
            FlushList();
            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        FlushList();
        return doc;
    }

    private static void AddInlines(InlineCollection target, string text)
    {
        var pos = 0;
        foreach (Match m in Inline().Matches(text))
        {
            if (m.Index > pos) target.Add(new Run(text[pos..m.Index]));
            var token = m.Value;
            if (token.StartsWith("**"))
            {
                var bold = new Bold { Foreground = Strong };
                AddInlines(bold.Inlines, token[2..^2]);
                target.Add(bold);
            }
            else if (token.StartsWith('`'))
            {
                target.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    Background = CodeBack,
                });
            }
            else
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, token[1..^1]);
                target.Add(italic);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) target.Add(new Run(text[pos..]));
    }
}
