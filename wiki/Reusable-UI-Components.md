# Reusable UI Components

## Shared Controls

### AsyncImageLoader

Loads images from URLs asynchronously without blocking the UI thread. Used for mod thumbnails, player skin/head avatars, and Modrinth project icons.

```csharp
public static class AsyncImageLoader
{
    public static async Task<BitmapSource?> LoadAsync(string url);
    public static BitmapSource? LoadFromWebP(byte[] webpBytes); // uses ImageSharp
}
```

Modrinth serves project thumbnails as WebP. `LoadFromWebP` uses `SixLabors.ImageSharp` to decode WebP and convert to `BitmapSource` for WPF.

---

### HealthIndicator

A small circular indicator that colors based on health score:
- 80–100 → green
- 50–79 → yellow
- 0–49 → red

Used on installation cards and in the health panel.

---

### ModCard

Displays a single mod result from search. Shows thumbnail, name, description, download count, platform badge (Modrinth / CurseForge), and an Install / Installed button.

---

### TerminalTextBox

A read-only `TextBox` with auto-scroll that appends lines of server/AI output. Optimized to avoid re-rendering the entire content on each append.

---

## Styles

### Theme.xaml (Dark, default)

Defines all `DynamicResource` keys:
- `BackgroundBrush`, `SurfaceBrush`, `PanelBrush`
- `TextBrush`, `TextSecondaryBrush`, `TextTertiaryBrush`
- `AccentBrush` (`#6C5DD3`)
- `BorderBrush`, `BorderLightBrush`
- Control styles: `Button`, `TextBox`, `ListBox`, `ComboBox`, `ToggleButton`, `TabControl`

### LightTheme.xaml

Overrides all the same keys with light variants. Merged in at runtime when `Theme = "Light"`.

---

## Converters

| Converter | Purpose |
|---|---|
| `BoolToVisibilityConverter` | `true` → `Visible`, `false` → `Collapsed` |
| `InverseBoolConverter` | Inverts a boolean |
| `NullToVisibilityConverter` | `null` → `Collapsed`, non-null → `Visible` |
| `HealthScoreToColorConverter` | `int` → `Brush` (green/yellow/red) |
| `LoaderToIconConverter` | Loader name string → loader icon `ImageSource` |
| `PlatformToColorConverter` | `"modrinth"` → green, `"curseforge"` → orange |
| `FileSizeConverter` | `long` bytes → human-readable string |

---

## ViewModelBase and RelayCommand

See [ViewModel Architecture and Data Binding](ViewModel-Architecture-and-Data-Binding) for the base class implementations used by all ViewModels.
