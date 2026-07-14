using System.Windows;
using System.Windows.Input;
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
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void VersionList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VersionList.SelectedItem is ContentVersionInfo v)
        {
            SelectedVersion = v;
            DialogResult = true;
        }
    }
}
