using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace MinecraftControlHub.UI.Converters;

/// <summary>
/// Converts Modrinth's Markdown/HTML project body into clean, readable plain text
/// suitable for a simple WPF TextBlock (we intentionally do not render full Markdown).
/// </summary>
public class MarkdownToPlainTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Drop fenced/inline code fences markers but keep their content readable.
        text = text.Replace("```", string.Empty);

        // Remove HTML tags (Modrinth bodies frequently embed raw HTML).
        text = Regex.Replace(text, "<[^>]+>", string.Empty);

        // Markdown images: ![alt](url) -> (removed)
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", string.Empty);

        // Markdown links: [label](url) -> label
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");

        // Headings: strip leading #'s
        text = Regex.Replace(text, @"(?m)^\s{0,3}#{1,6}\s*", string.Empty);

        // Blockquotes and list bullets markers at line start.
        text = Regex.Replace(text, @"(?m)^\s{0,3}>\s?", string.Empty);
        text = Regex.Replace(text, @"(?m)^\s{0,3}[-*+]\s+", "• ");

        // Emphasis / inline code markers.
        text = text.Replace("**", string.Empty)
                   .Replace("__", string.Empty)
                   .Replace("`", string.Empty);
        text = Regex.Replace(text, @"(?<!\w)[*_](?=\S)|(?<=\S)[*_](?!\w)", string.Empty);

        // Horizontal rules.
        text = Regex.Replace(text, @"(?m)^\s*([-*_]\s*){3,}$", string.Empty);

        // Common HTML entities.
        text = text.Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&quot;", "\"")
                   .Replace("&#39;", "'")
                   .Replace("&nbsp;", " ");

        // Collapse excessive blank lines and trailing whitespace.
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null or "" ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Shows the loader-version picker only once a real mod loader (not Vanilla) is selected — vanilla has no loader builds to pick from.</summary>
public class LoaderNotVanillaToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MinecraftControlHub.Core.Models.LoaderType.Vanilla ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.45;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToEnableTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Click to disable this mod" : "Click to enable this mod";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts IsEnabled (bool) to "Enabled" / "Disabled" label text for content item toggle buttons.</summary>
public class BoolToEnabledLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Enabled" : "Disabled";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// IMultiValueConverter that returns true when all bound values are equal.
/// Used for highlighting the active pagination button without using Binding on DataTrigger.Value.
/// Usage: &lt;MultiBinding Converter="{StaticResource EqualityConverter}"&gt; ... &lt;/MultiBinding&gt;
/// </summary>
public class EqualityConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2) return false;
        return Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a TunnelProviderTier to the matching background Brush for the tier badge.
/// Used in TunnelPage provider pickers.
/// </summary>
public class TierToBadgeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MinecraftControlHub.Core.Models.TunnelProviderTier tier)
        {
            return tier switch
            {
                MinecraftControlHub.Core.Models.TunnelProviderTier.Free             => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x7A, 0x46)),
                MinecraftControlHub.Core.Models.TunnelProviderTier.FreemiumLimited  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x5C, 0x1E)),
                MinecraftControlHub.Core.Models.TunnelProviderTier.Premium          => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x3A, 0x7A)),
                _                                                                   => System.Windows.Media.Brushes.Transparent
            };
        }
        return System.Windows.Media.Brushes.Transparent;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a TunnelProviderTier to the matching foreground Brush for the tier badge text.
/// </summary>
public class TierToTextBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MinecraftControlHub.Core.Models.TunnelProviderTier tier)
        {
            return tier switch
            {
                MinecraftControlHub.Core.Models.TunnelProviderTier.Free             => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0xE8, 0x9A)),
                MinecraftControlHub.Core.Models.TunnelProviderTier.FreemiumLimited  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xC0, 0x60)),
                MinecraftControlHub.Core.Models.TunnelProviderTier.Premium          => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0x9A, 0xF0)),
                _                                                                   => System.Windows.Media.Brushes.White
            };
        }
        return System.Windows.Media.Brushes.White;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

