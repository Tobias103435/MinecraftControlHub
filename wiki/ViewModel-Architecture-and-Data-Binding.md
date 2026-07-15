# ViewModel Architecture and Data Binding

## ViewModelBase

All ViewModels inherit from `ViewModelBase`:

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

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

---

## Property Pattern

```csharp
private bool _isLoading;
public bool IsLoading
{
    get => _isLoading;
    set => SetProperty(ref _isLoading, value);
}
```

`SetProperty` returns `false` (and fires no event) if the value hasn't changed — avoids unnecessary UI re-renders.

---

## RelayCommand

```csharp
public ICommand CreateCommand { get; }

public HomePageViewModel(IInstallationService svc)
{
    CreateCommand = new RelayCommand(_ => OpenCreateDialog());
}
```

For async operations:

```csharp
public ICommand LoadCommand { get; }

public HomePageViewModel(IInstallationService svc)
{
    LoadCommand = new AsyncRelayCommand(_ => LoadInstallationsAsync());
}

private async Task LoadInstallationsAsync()
{
    IsLoading = true;
    try
    {
        var list = await _svc.GetAllInstallationsAsync();
        Installations.Clear();
        foreach (var item in list)
            Installations.Add(new InstallationViewModel(item));
    }
    finally { IsLoading = false; }
}
```

---

## XAML Binding

```xml
<ListBox ItemsSource="{Binding Installations}" SelectedItem="{Binding SelectedInstallation}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel>
                <TextBlock Text="{Binding Name}" Style="{DynamicResource BodyTextStyle}" />
                <TextBlock Text="{Binding Version}" Style="{DynamicResource CaptionTextStyle}" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>

<Button Command="{Binding CreateCommand}" Content="New Installation" />
```

---

## DI and ViewModel Resolution

ViewModels are registered as transient in the DI container. Pages receive their ViewModel through constructor injection:

```csharp
public partial class HomePage : UserControl
{
    public HomePageViewModel ViewModel { get; }

    public HomePage(HomePageViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = ViewModel;
    }
}
```

---

## Thread Safety Pattern

Service events fire on background threads. ViewModels always dispatch UI updates back to the UI thread:

```csharp
_service.InstallationsChanged += (_, _) =>
    App.Current.Dispatcher.InvokeAsync(async () => await LoadInstallationsAsync());
```
