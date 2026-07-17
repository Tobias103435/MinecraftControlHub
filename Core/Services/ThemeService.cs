using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace MinecraftControlHub.Core.Services;

/// <summary>
/// Switches the app theme between Dark and Light.
///
/// Controls.axaml and the structural XAML (Sidebar, MainWindow) use {DynamicResource}
/// for all brush keys, so mutating the brush objects in Application.Resources is
/// sufficient — every bound element repaints automatically without any layout tricks.
/// </summary>
public static class ThemeService
{
    // ── Dark palette ──────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Color> Dark = new()
    {
        ["ColorAppBackground"]     = Color.FromRgb(0x15, 0x16, 0x1A),
        ["ColorSidebarBackground"] = Color.FromRgb(0x1B, 0x1D, 0x23),
        ["ColorPanelBackground"]   = Color.FromRgb(0x22, 0x25, 0x2B),
        ["ColorPanelElevated"]     = Color.FromRgb(0x2A, 0x2E, 0x36),
        ["ColorBorder"]            = Color.FromRgb(0x33, 0x36, 0x3F),
        ["ColorAccent"]            = Color.FromRgb(0x6C, 0x5D, 0xD3),
        ["ColorAccentHover"]       = Color.FromRgb(0x81, 0x72, 0xE0),
        ["ColorOnline"]            = Color.FromRgb(0x57, 0xC7, 0x85),
        ["ColorOffline"]           = Color.FromRgb(0x7A, 0x7F, 0x8A),
        ["ColorDanger"]            = Color.FromRgb(0xE5, 0x53, 0x4B),
        ["ColorTextPrimary"]       = Color.FromRgb(0xE8, 0xE9, 0xED),
        ["ColorTextSecondary"]     = Color.FromRgb(0x90, 0x98, 0xA3),
        ["ColorTextMuted"]         = Color.FromRgb(0x5C, 0x62, 0x70),
        ["ColorInfoBg"]            = Color.FromRgb(0x0E, 0x22, 0x33),
        ["ColorInfoText"]          = Color.FromRgb(0x60, 0xAA, 0xEE),
    };

    // ── Light palette ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Color> Light = new()
    {
        ["ColorAppBackground"]     = Color.FromRgb(0xF0, 0xF1, 0xF5),
        ["ColorSidebarBackground"] = Color.FromRgb(0xE4, 0xE6, 0xEC),
        ["ColorPanelBackground"]   = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["ColorPanelElevated"]     = Color.FromRgb(0xF5, 0xF6, 0xFA),
        ["ColorBorder"]            = Color.FromRgb(0xD0, 0xD3, 0xDC),
        ["ColorAccent"]            = Color.FromRgb(0x6C, 0x5D, 0xD3),
        ["ColorAccentHover"]       = Color.FromRgb(0x81, 0x72, 0xE0),
        ["ColorOnline"]            = Color.FromRgb(0x2E, 0x9E, 0x5A),
        ["ColorOffline"]           = Color.FromRgb(0x8A, 0x90, 0x99),
        ["ColorDanger"]            = Color.FromRgb(0xD9, 0x30, 0x25),
        ["ColorTextPrimary"]       = Color.FromRgb(0x11, 0x13, 0x18),
        ["ColorTextSecondary"]     = Color.FromRgb(0x4A, 0x52, 0x60),
        ["ColorTextMuted"]         = Color.FromRgb(0x8A, 0x92, 0xA0),
        ["ColorInfoBg"]            = Color.FromRgb(0xE3, 0xF2, 0xFD),
        ["ColorInfoText"]          = Color.FromRgb(0x19, 0x76, 0xD2),
    };

    private static readonly Dictionary<string, string> BrushToColor = new()
    {
        ["BrushAppBackground"]     = "ColorAppBackground",
        ["BrushSidebarBackground"] = "ColorSidebarBackground",
        ["BrushPanelBackground"]   = "ColorPanelBackground",
        ["BrushPanelElevated"]     = "ColorPanelElevated",
        ["BrushBorder"]            = "ColorBorder",
        ["BrushAccent"]            = "ColorAccent",
        ["BrushAccentHover"]       = "ColorAccentHover",
        ["BrushOnline"]            = "ColorOnline",
        ["BrushOffline"]           = "ColorOffline",
        ["BrushDanger"]            = "ColorDanger",
        ["BrushTextPrimary"]       = "ColorTextPrimary",
        ["BrushTextSecondary"]     = "ColorTextSecondary",
        ["BrushTextMuted"]         = "ColorTextMuted",
        ["BrushInfoBg"]            = "ColorInfoBg",
        ["BrushInfoText"]          = "ColorInfoText",
    };

    public static void ApplySavedTheme(ISettingsService settings)
        => Apply(settings.Settings.Theme == "Light");

    public static async Task ToggleAsync(ISettingsService settings)
    {
        var goLight = settings.Settings.Theme != "Light";
        Apply(goLight);
        settings.Settings.Theme = goLight ? "Light" : "Dark";
        await settings.SaveAsync();
    }

    public static bool IsLight(ISettingsService settings)
        => settings.Settings.Theme == "Light";

    private static void Apply(bool light)
    {
        var palette = light ? Light : Dark;
        var res     = Application.Current!.Resources;

        // 1. Update Color resources (for any remaining {DynamicResource ColorXxx} users)
        foreach (var kv in palette)
            res[kv.Key] = kv.Value;

        // 2. Mutate or replace the SolidColorBrush objects.
        //    Because Controls.axaml/Sidebar/MainWindow now use {DynamicResource BrushXxx},
        //    Avalonia's DynamicResource infrastructure detects the resource change and
        //    automatically re-evaluates every binding to that key — no layout nudge needed.
        //    Unlike WPF, Avalonia brushes are never "frozen", so they can always be mutated
        //    in place.
        foreach (var kv in BrushToColor)
        {
            if (!palette.TryGetValue(kv.Value, out var color)) continue;

            if (res.TryGetResource(kv.Key, null, out var existing) && existing is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                res[kv.Key] = new SolidColorBrush(color);
            }
        }

        // 3. Update any open window backgrounds that are set directly in C#
        if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (res.TryGetResource("BrushAppBackground", null, out var bgObj) && bgObj is SolidColorBrush bg)
            {
                foreach (var w in desktop.Windows)
                    w.Background = bg;
            }
        }
    }
}
