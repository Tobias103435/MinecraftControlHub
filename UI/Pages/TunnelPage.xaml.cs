using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Models;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;
using MinecraftControlHub.UI.Windows;

namespace MinecraftControlHub.UI.Pages;

public partial class TunnelPage : UserControl
{
    private TunnelPageViewModel? ViewModel => DataContext as TunnelPageViewModel;

    /// <summary>
    /// Exposed to XAML so the "Share Tunnel" button is only visible when signed in to Nexora.
    /// </summary>
    public bool IsNexoraLoggedIn { get; private set; }

    public TunnelPage()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current!).ServiceProvider?.GetService(typeof(TunnelPageViewModel));

        var nexoraService = ((App)Application.Current!).ServiceProvider?.GetService<INexoraAccountService>();
        if (nexoraService != null)
        {
            IsNexoraLoggedIn = nexoraService.Current != null;
            nexoraService.AccountChanged += (_, _) =>
            {
                IsNexoraLoggedIn = nexoraService.Current != null;
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsNexoraLoggedIn)));
            };
        }
    }

    // 'new' because AvaloniaObject already declares a PropertyChanged event for its
    // own AvaloniaProperty system; this one is specifically for the plain
    // IsNexoraLoggedIn CLR property below, used by the $parent[UserControl] binding
    // in TunnelPage.axaml.
    public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    // -----------------------------------------------------------------------
    // Custom-port actions
    // -----------------------------------------------------------------------

    private void AddCustomPort_Click(object? sender, RoutedEventArgs e)
        => ViewModel?.AddCustomPort();

    private void RemovePort_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PortInfo port)
            ViewModel?.RemovePort(port);
    }

    // -----------------------------------------------------------------------
    // Inline provider picker injected into each port row's ContentControl
    // -----------------------------------------------------------------------

    private void PortRow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContentControl cc) return;
        if (cc.Tag is not PortInfo portInfo) return;
        if (ViewModel is null) return;

        RefreshInlineCombo(cc, portInfo);

        ViewModel.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(TunnelPageViewModel.Assignments))
                RefreshInlineCombo(cc, portInfo);
        };
    }

    private void RefreshInlineCombo(ContentControl cc, PortInfo portInfo)
    {
        var assignment = ViewModel?.Assignments.FirstOrDefault(a => a.Port == portInfo);

        if (assignment is null || !portInfo.IsEnabled)
        {
            cc.Content = new TextBlock
            {
                Text              = "—",
                FontSize          = 12,
                Foreground        = TryFindBrush("BrushTextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }

        var compatible = ViewModel!.GetCompatibleProviders(portInfo.Protocol);

        var cb = new ComboBox
        {
            Height       = 34,
            FontSize     = 12,
            ItemsSource  = compatible,
            SelectedItem = assignment.SelectedProvider,
            ItemTemplate = new FuncDataTemplate<TunnelProvider>((_, _) =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };

                var nameText = new TextBlock
                {
                    FontSize          = 12,
                    Foreground        = TryFindBrush("BrushTextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameText.Bind(TextBlock.TextProperty, new Binding("DisplayName"));

                var priceText = new TextBlock
                {
                    FontSize          = 10,
                    Foreground        = TryFindBrush("BrushTextMuted"),
                    Margin            = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                priceText.Bind(TextBlock.TextProperty, new Binding("PriceLabel"));

                panel.Children.Add(nameText);
                panel.Children.Add(priceText);
                return panel;
            })
        };

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is TunnelProvider p)
                assignment.SelectedProvider = p;
        };

        assignment.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(PortTunnelAssignment.SelectedProvider)
                && cb.SelectedItem != assignment.SelectedProvider)
                cb.SelectedItem = assignment.SelectedProvider;
        };

        cc.Content = cb;
    }

    // -----------------------------------------------------------------------
    // playit.gg claim URL
    // -----------------------------------------------------------------------

    private void OpenClaimUrl_Click(object? sender, RoutedEventArgs e)
    {
        var url = (sender as Button)?.Tag as string;
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // -----------------------------------------------------------------------
    // Log / address copy
    // -----------------------------------------------------------------------

    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string log && !string.IsNullOrEmpty(log))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                try { await clipboard.SetTextAsync(log); }
                catch { }
            }
        }
    }

    // NOTE: this used to be a Command bound to TunnelSessionViewModel.CopyAddressCommand,
    // but Avalonia's clipboard is per-TopLevel/window, not a static class the ViewModel
    // could reach — so, like Copy log above, it's a plain view-level click handler now.
    private async void CopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TunnelSessionViewModel svm } && svm.HasAddress)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                try { await clipboard.SetTextAsync(svm.PublicAddress!); }
                catch { }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Share Tunnel — opens friend-selection dialog
    // -----------------------------------------------------------------------

    private async void ShareTunnel_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Tag is bound to {Binding} — the TunnelSessionViewModel for this row
        if (btn.Tag is not TunnelSessionViewModel session) return;
        if (string.IsNullOrEmpty(session.PublicAddress)) return;

        var serverName = ViewModel?.SelectedServer?.Name;
        var owner = TopLevel.GetTopLevel(this) as Window;

        var win = new ShareTunnelWindow(session.PublicAddress, serverName);
        if (owner != null)
            await win.ShowDialog(owner);
        else
            win.Show();
    }

    // -----------------------------------------------------------------------

    private IBrush TryFindBrush(string key)
    {
        if (this.TryFindResource(key, out var value) && value is IBrush brush)
            return brush;
        return Brushes.Transparent;
    }
}
