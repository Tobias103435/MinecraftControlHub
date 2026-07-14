using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        DataContext = ((App)Application.Current).ServiceProvider?.GetService(typeof(TunnelPageViewModel));

        var nexoraService = ((App)Application.Current).ServiceProvider?.GetService<INexoraAccountService>();
        if (nexoraService != null)
        {
            IsNexoraLoggedIn = nexoraService.Current != null;
            nexoraService.AccountChanged += (_, _) =>
            {
                IsNexoraLoggedIn = nexoraService.Current != null;
                Dispatcher.Invoke(() => OnPropertyChanged(nameof(IsNexoraLoggedIn)));
            };
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    // -----------------------------------------------------------------------
    // Custom-port actions
    // -----------------------------------------------------------------------

    private void AddCustomPort_Click(object sender, RoutedEventArgs e)
        => ViewModel?.AddCustomPort();

    private void RemovePort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PortInfo port)
            ViewModel?.RemovePort(port);
    }

    // -----------------------------------------------------------------------
    // Inline provider picker injected into each port row's ContentControl
    // -----------------------------------------------------------------------

    private void PortRow_Loaded(object sender, RoutedEventArgs e)
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
            SelectedItem = assignment.SelectedProvider
        };

        var itemFactory = new FrameworkElementFactory(typeof(StackPanel));
        itemFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
        nameFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
        nameFactory.SetValue(TextBlock.ForegroundProperty, TryFindBrush("BrushTextPrimary"));
        nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var priceFactory = new FrameworkElementFactory(typeof(TextBlock));
        priceFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("PriceLabel"));
        priceFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        priceFactory.SetValue(TextBlock.ForegroundProperty, TryFindBrush("BrushTextMuted"));
        priceFactory.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        priceFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        itemFactory.AppendChild(nameFactory);
        itemFactory.AppendChild(priceFactory);
        cb.ItemTemplate = new DataTemplate { VisualTree = itemFactory };

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

    private void OpenClaimUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = (sender as Button)?.Tag as string;
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // -----------------------------------------------------------------------
    // Log copy
    // -----------------------------------------------------------------------

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string log && !string.IsNullOrEmpty(log))
        {
            try { System.Windows.Clipboard.SetText(log); }
            catch { }
        }
    }

    // -----------------------------------------------------------------------
    // Share Tunnel — opens friend-selection dialog
    // -----------------------------------------------------------------------

    private void ShareTunnel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Tag is bound to {Binding} — the TunnelSessionViewModel for this row
        if (btn.Tag is not TunnelSessionViewModel session) return;
        if (string.IsNullOrEmpty(session.PublicAddress)) return;

        var serverName = ViewModel?.SelectedServer?.Name;

        var win = new ShareTunnelWindow(session.PublicAddress, serverName)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();
    }

    // -----------------------------------------------------------------------

    private Brush TryFindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Transparent;
}
