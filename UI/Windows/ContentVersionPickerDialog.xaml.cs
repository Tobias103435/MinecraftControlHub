using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.Windows;

public partial class ContentVersionPickerDialog : Window
{
    public ContentVersionInfo? SelectedVersion { get; private set; }

    public ContentVersionPickerDialog(List<ContentVersionInfo> versions)
    {
        InitializeComponent();
        VersionList.ItemsSource = versions;
        if (versions.Count > 0)
            VersionList.SelectedIndex = 0;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        SelectedVersion = VersionList.SelectedItem as ContentVersionInfo;
        if (SelectedVersion == null) return;
        Close(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Close(false);

    private void VersionList_DoubleClick(object sender, PointerPressedEventArgs e)
    {
        if (VersionList.SelectedItem is ContentVersionInfo v)
        {
            SelectedVersion = v;
            Close(true);
        }
    }
}
