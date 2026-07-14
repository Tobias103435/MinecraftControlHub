using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.AI.Models;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Pages;

public class TerminalMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate            { get; set; }
    public DataTemplate? AITemplate              { get; set; }
    public DataTemplate? ActionPlanTemplate      { get; set; }
    public DataTemplate? ExecutionResultTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not TerminalMessage msg) return base.SelectTemplate(item, container);

        return msg.Type switch
        {
            TerminalMessageType.User            => UserTemplate,
            TerminalMessageType.AI              => AITemplate,
            TerminalMessageType.ActionPlan      => ActionPlanTemplate,
            TerminalMessageType.ExecutionResult => ExecutionResultTemplate,
            _                                   => base.SelectTemplate(item, container)
        };
    }
}

public partial class AiPage : UserControl
{
    private AiTerminalViewModel? _viewModel;

    public AiPage()
    {
        InitializeComponent();

        var serviceProvider = (Application.Current as App)?.ServiceProvider;
        if (serviceProvider != null)
        {
            _viewModel = serviceProvider.GetRequiredService<AiTerminalViewModel>();
            DataContext = _viewModel;
        }

        if (_viewModel != null)
            _viewModel.Messages.CollectionChanged += (_, _) => ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() =>
            MessageScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.SendAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.Cancel();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearHistory();
    }

    private async void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyboardDevice.IsKeyDown(Key.LeftShift))
        {
            e.Handled = true;
            if (_viewModel != null)
                await _viewModel.SendAsync();
        }
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel?.NotifyInputChanged();
    }

    private async void ExecuteAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TerminalMessage msg } && _viewModel != null)
            await _viewModel.ExecuteActionAsync(msg);
    }

    private void CancelAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TerminalMessage msg })
            _viewModel?.CancelAction(msg);
    }

    private void ImportModFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title       = "Select mod .jar to import",
            Filter      = "Mod files (*.jar)|*.jar|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var fileName = System.IO.Path.GetFileName(path);

        // Pre-fill the input box so the user can specify the target installation
        if (_viewModel != null)
        {
            _viewModel.InputText =
                $"Import mod from file \"{path}\" into ";
            InputBox.Focus();
            InputBox.CaretIndex = _viewModel.InputText.Length;
        }
    }

    private async void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = GeminiKeyBox.Text;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SaveStatusText.Text = "Please enter an API key.";
            SaveStatusText.Visibility = Visibility.Visible;
            SaveStatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        var settingsService = (Application.Current as App)?.ServiceProvider?.GetService<MinecraftControlHub.Core.Services.ISettingsService>();
        if (settingsService == null) return;

        // Save the API key
        settingsService.Settings.AiApiKey = apiKey;
        settingsService.Settings.AiModel = "gemini-3.1-flash-lite";
        await settingsService.SaveAsync();

        // Update the ViewModel to reflect the new configuration
        if (_viewModel != null)
        {
            await _viewModel.ReloadConfigurationAsync();
        }

        SaveStatusText.Text = "API key saved! Using model: gemini-3.1-flash-lite";
        SaveStatusText.Visibility = Visibility.Visible;
        SaveStatusText.Foreground = System.Windows.Media.Brushes.Green;
        GeminiKeyBox.Text = string.Empty;
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
