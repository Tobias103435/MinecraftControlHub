# MVVM Pattern Implementation

## Overview

The UI layer follows the Model-View-ViewModel (MVVM) pattern. Every page has a corresponding ViewModel that holds all logic and state. The View (XAML) only contains layout and bindings — no logic.

---

## ViewModelBase

All ViewModels inherit from `ViewModelBase`, which implements `INotifyPropertyChanged`:

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

Usage in a ViewModel:

```csharp
private bool _isLoading;
public bool IsLoading
{
    get => _isLoading;
    set => SetProperty(ref _isLoading, value);
}
```

---

## RelayCommand

Commands are implemented with `RelayCommand` (sync) and `AsyncRelayCommand` (async):

```csharp
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

Async variant wraps the async action and sets `IsLoading` during execution:

```csharp
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private bool _isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _execute(parameter); }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

---

## Data Binding Patterns

### Collections

```csharp
public ObservableCollection<InstallationViewModel> Installations { get; } = new();
```

Bound in XAML:
```xml
<ListBox ItemsSource="{Binding Installations}" />
```

### Commands

```csharp
public ICommand CreateInstallationCommand { get; }

public HomePageViewModel(IInstallationService svc)
{
    CreateInstallationCommand = new AsyncRelayCommand(_ => CreateInstallationAsync());
}
```

Bound in XAML:
```xml
<Button Command="{Binding CreateInstallationCommand}" Content="New Installation" />
```

### Converters

Common converters used across the app:

| Converter | Converts |
|---|---|
| `BoolToVisibilityConverter` | `bool` → `Visibility` |
| `InverseBoolConverter` | `bool` → `!bool` |
| `NullToVisibilityConverter` | `null` → `Collapsed` |
| `HealthScoreToColorConverter` | `int` → `Brush` (green/yellow/red) |

---

## Thread Safety

ViewModels subscribe to service events that may fire from background threads. All UI updates must be dispatched to the UI thread:

```csharp
_installationService.InstallationsChanged += (_, _) =>
{
    App.Current.Dispatcher.InvokeAsync(async () => await LoadInstallationsAsync());
};
```

`TerminalMessage.IsDone` and other observable properties on `TerminalMessage` are set via `Dispatcher.InvokeAsync` internally, so they are safe to set from background streaming tasks.
