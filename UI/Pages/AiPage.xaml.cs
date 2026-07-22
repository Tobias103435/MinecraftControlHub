using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.AI.Models;
using MinecraftControlHub.UI.ViewModels;

namespace MinecraftControlHub.UI.Pages;

public class TerminalMessageTemplateSelector : IDataTemplate
{
    public DataTemplate? UserTemplate            { get; set; }
    public DataTemplate? AITemplate              { get; set; }
    public DataTemplate? ActionPlanTemplate      { get; set; }
    public DataTemplate? ExecutionResultTemplate { get; set; }

    public Control Build(object? data)
    {
        if (data is not TerminalMessage msg) return new TextBlock();
        var template = msg.Type switch
        {
            TerminalMessageType.User            => UserTemplate,
            TerminalMessageType.AI              => AITemplate,
            TerminalMessageType.ActionPlan      => ActionPlanTemplate,
            TerminalMessageType.ExecutionResult => ExecutionResultTemplate,
            _                                   => null
        };
        return template?.Build(data) ?? new TextBlock { Text = msg.Text };
    }

    public bool Match(object? data) => data is TerminalMessage;
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
        Dispatcher.UIThread.Post(async () =>
        {
            // Wait a little for everything to settle
            await Task.Delay(50);
            
            if (_viewModel?.Messages.Count > 0)
            {
                var lastIndex = _viewModel.Messages.Count - 1;
                MessageList.ScrollIntoView(_viewModel.Messages[lastIndex]);
            }
        }, DispatcherPriority.Input);
    }

    private void MessageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem != null)
            listBox.SelectedItem = null;
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
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TerminalMessage msg })
            msg.IsExpanded = !msg.IsExpanded;
    }

    /// <summary>
    /// "Fix with AI" button click — sends the failed execution result back to the AI
    /// so it can analyse the errors and propose alternative actions (e.g. different mod).
    /// </summary>
    private async void FixWithAi_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || _viewModel == null) return;

        // The button lives inside a DataTemplate, so its DataContext is the TerminalMessage.
        if (btn.DataContext is not TerminalMessage msg) return;

        // Disable the button so the user cannot spam it.
        btn.IsEnabled = false;
        btn.Content   = "⏳ Analyzing…";

        await _viewModel.AskAiAboutErrorAsync(msg.Text);
    }

    private async void ImportModFromFile_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select mod .jar to import",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mod files") { Patterns = new[] { "*.jar" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files == null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;

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
            SaveStatusText.IsVisible = true;
            SaveStatusText.Foreground = Avalonia.Media.Brushes.Red;
            return;
        }

        var settingsService = (Application.Current as App)?.ServiceProvider?.GetService<MinecraftControlHub.Core.Services.ISettingsService>();
        if (settingsService == null) return;

        // Save the API key — always force Gemini provider + correct model
        settingsService.Settings.AiProvider = "Gemini";
        settingsService.Settings.AiApiKey = apiKey;
        settingsService.Settings.AiModel = "gemini-3.1-flash-lite";
        await settingsService.SaveAsync();

        // Update the ViewModel to reflect the new configuration
        if (_viewModel != null)
        {
            await _viewModel.ReloadConfigurationAsync();
        }

        SaveStatusText.Text = "API key saved! Using model: gemini-3.1-flash-lite";
        SaveStatusText.IsVisible = true;
        SaveStatusText.Foreground = Avalonia.Media.Brushes.Green;
        GeminiKeyBox.Text = string.Empty;
    }

    private void Hyperlink_RequestNavigate(object sender, TappedEventArgs e)
    {
        if (sender is HyperlinkButton hb && hb.NavigateUri != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = hb.NavigateUri.ToString(),
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}

