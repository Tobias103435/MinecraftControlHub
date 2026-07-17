using System.Globalization;
using Avalonia.Data.Converters;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Tiny value converter used in place of WPF's Style/DataTrigger pattern
/// ("if Property == X then ..."). Returns true when value.ToString() equals
/// the ConverterParameter (case-insensitive). Used with Avalonia's
/// Classes.xxx="{Binding ..., Converter=..., ConverterParameter=...}" to
/// drive Style Selector-based visuals instead of DataTriggers, which don't
/// exist in Avalonia.
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString()?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
