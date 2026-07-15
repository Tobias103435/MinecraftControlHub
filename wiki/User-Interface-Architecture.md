# User Interface Architecture

## Overview

The UI layer is built with WPF (.NET 8). It follows MVVM strictly — all logic lives in ViewModels, all layouts live in XAML, and the two communicate exclusively through data binding and commands.

---

## Layer Structure

```
UI/
  Pages/           ← UserControl pages shown in MainWindow's content area
  ViewModels/      ← ViewModel for each page and window
  Windows/         ← Dialog windows (separate top-level windows)
  Components/      ← Reusable UserControl components
  Styles/          ← Theme.xaml, LightTheme.xaml, shared control styles
  Converters/      ← Value converters (BoolToVisibility, HealthScoreToColor, etc.)
  Helpers/         ← Static utilities (AsyncImageLoader, etc.)
```

---

## Navigation

`MainWindow` uses a `Dictionary<AppPage, UserControl>` cache. Pages are created once (on first visit) and reused:

```csharp
public enum AppPage
{
    Home, Servers, Mods, Content, Friends, Account, Tunnel, AI, Settings
}
```

The `Sidebar` control raises `PageSelected` events. `MainWindow` handles them via `ShowPage()`, which swaps the `ContentControl.Content`.

---

## Theme System

Two themes are provided:
- `Styles/Theme.xaml` — dark theme (default)
- `Styles/LightTheme.xaml` — light theme

Switching is done by replacing the first `MergedDictionary` entry at runtime. All controls use `DynamicResource` so they update immediately without requiring a restart.

---

## Responsive Behavior

Most pages use `Grid` and `StackPanel` with fixed column widths. The window has a minimum size of 1000×650. The sidebar is always visible — there is no mobile/compact mode.

---

## Related Pages

- [Main Window and Navigation System](Main-Window-and-Navigation-System)
- [Core Application Pages](Core-Application-Pages)
- [Dialog Windows and Specialized Interfaces](Dialog-Windows-and-Specialized-Interfaces)
- [Reusable UI Components](Reusable-UI-Components)
- [ViewModel Architecture and Data Binding](ViewModel-Architecture-and-Data-Binding)
