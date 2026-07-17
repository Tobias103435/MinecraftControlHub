using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Windows;

public partial class ContentBrowserWindow : Window
{
    private readonly ContentBrowserViewModel _viewModel;

    public ContentBrowserWindow(ContentBrowserViewModel viewModel)
    {
        _viewModel  = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
        => await _viewModel.SearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _viewModel.SearchAsync();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is ContentSearchResult r)
            await _viewModel.InstallAsync(r);
    }

    private async void CheckDeps_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CheckDependenciesAsync();

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
        => await _viewModel.PreviousPageAsync();

    private async void NextPage_Click(object sender, RoutedEventArgs e)
        => await _viewModel.NextPageAsync();

    private async void PageNumber_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is int page)
            await _viewModel.GoToPageAsync(page);
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, PointerWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv) return;
        sv.Offset = new Avalonia.Vector(sv.Offset.X, sv.Offset.Y - e.Delta.Y);
        e.Handled = true;
    }
}
