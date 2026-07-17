using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Avalonia attached property that converts plain text containing URLs into a TextBlock
/// with clickable hyperlink-styled inlines.  Usage in AXAML:
///   &lt;TextBlock helpers:TextLinkHelper.TextWithLinks="{Binding Text}" /&gt;
/// </summary>
public static class TextLinkHelper
{
    private static readonly Regex UrlRegex = new(
        @"(https?://[^\s\])<,""]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly AttachedProperty<string> TextWithLinksProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string>(
            "TextWithLinks",
            typeof(TextLinkHelper),
            defaultValue: string.Empty);

    public static readonly AttachedProperty<IBrush?> LinkBrushProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>(
            "LinkBrush",
            typeof(TextLinkHelper),
            defaultValue: null);

    static TextLinkHelper()
    {
        TextWithLinksProperty.Changed.AddClassHandler<TextBlock>(OnTextWithLinksChanged);
    }

    public static void SetTextWithLinks(TextBlock obj, string value)
        => obj.SetValue(TextWithLinksProperty, value);

    public static string GetTextWithLinks(TextBlock obj)
        => obj.GetValue(TextWithLinksProperty);

    public static void SetLinkBrush(TextBlock obj, IBrush? value)
        => obj.SetValue(LinkBrushProperty, value);

    public static IBrush? GetLinkBrush(TextBlock obj)
        => obj.GetValue(LinkBrushProperty);

    private static void OnTextWithLinksChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
    {
        var text = e.NewValue as string ?? string.Empty;

        textBlock.Inlines?.Clear();
        if (textBlock.Inlines == null) return;

        if (string.IsNullOrEmpty(text)) return;

        var matches = UrlRegex.Matches(text);
        if (matches.Count == 0)
        {
            textBlock.Inlines.Add(new Run(text));
            return;
        }

        var linkBrush = GetLinkBrush(textBlock)
                        ?? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));

        var lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(new Run(text[lastIndex..match.Index]));
            }

            var url = match.Value;
            var link = new HyperlinkButton
            {
                Content = url,
                NavigateUri = new Uri(url, UriKind.Absolute),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Foreground = linkBrush,
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = textBlock.FontSize,
                FontFamily = textBlock.FontFamily
            };
            // Use inline style: make it look like an inline hyperlink
            link.Classes.Add("InlineHyperlink");
            textBlock.Inlines.Add(link);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(new Run(text[lastIndex..]));
        }
    }
}
