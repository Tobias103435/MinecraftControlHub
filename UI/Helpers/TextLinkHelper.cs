using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Attached property that converts plain text containing URLs into a TextBlock
/// with clickable Hyperlink inlines.  Usage in XAML:
///   &lt;TextBlock helpers:TextLinkHelper.TextWithLinks="{Binding Text}" /&gt;
/// </summary>
public static class TextLinkHelper
{
    private static readonly Regex UrlRegex = new(
        @"(https?://[^\s\])<,""]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly DependencyProperty TextWithLinksProperty =
        DependencyProperty.RegisterAttached(
            "TextWithLinks",
            typeof(string),
            typeof(TextLinkHelper),
            new PropertyMetadata(string.Empty, OnTextWithLinksChanged));

    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.RegisterAttached(
            "LinkBrush",
            typeof(Brush),
            typeof(TextLinkHelper),
            new PropertyMetadata(null));

    public static void SetTextWithLinks(DependencyObject obj, string value)
        => obj.SetValue(TextWithLinksProperty, value);

    public static string GetTextWithLinks(DependencyObject obj)
        => (string)obj.GetValue(TextWithLinksProperty);

    public static void SetLinkBrush(DependencyObject obj, Brush value)
        => obj.SetValue(LinkBrushProperty, value);

    public static Brush? GetLinkBrush(DependencyObject obj)
        => obj.GetValue(LinkBrushProperty) as Brush;

    private static void OnTextWithLinksChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;
        var text = e.NewValue as string ?? string.Empty;

        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(text)) return;

        var matches = UrlRegex.Matches(text);
        if (matches.Count == 0)
        {
            // No links — just plain text
            textBlock.Inlines.Add(new Run(text));
            return;
        }

        var linkBrush = GetLinkBrush(textBlock)
                        ?? new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)); // default accent blue

        var lastIndex = 0;
        foreach (Match match in matches)
        {
            // Add plain text before the URL
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(
                    new Run(text[lastIndex..match.Index]));
            }

            // Add clickable hyperlink
            var url = match.Value;
            var hyperlink = new Hyperlink(new Run(url))
            {
                NavigateUri    = new Uri(url, UriKind.Absolute),
                Foreground     = linkBrush,
                TextDecorations = TextDecorations.Underline
            };
            hyperlink.RequestNavigate += HyperlinkOnRequestNavigate;
            textBlock.Inlines.Add(hyperlink);

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after the last URL
        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(new Run(text[lastIndex..]));
        }
    }

    private static void HyperlinkOnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
