using MinecraftControlHub.AI.Services;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public partial class SettingsPageViewModel
{
    private readonly IAIService _aiService;

    private string _aiApiKey = string.Empty;
    private bool _isLoadingGeminiModels;
    private string _aiSettingsStatus = string.Empty;

    private static readonly Dictionary<string, List<string>> ProviderModels = new()
    {
        ["OpenAI"] = new List<string>
        {
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-4-turbo",
            "gpt-3.5-turbo"
        },
        ["Gemini"] = new List<string>
        {
            // Populated dynamically via GetGeminiModelsAsync when the user saves their API key.
            // These are fallbacks shown before a key is entered.
            "gemini-2.5-flash",
            "gemini-2.5-pro"
        },
        ["Custom"] = new List<string>
        {
            "gpt-4o-mini",
            "gpt-4o",
            "mistral-7b-instruct",
            "llama-3-8b-instruct"
        }
    };

    public List<string> AiProviders { get; } = new() { "OpenAI", "Gemini", "Custom" };

    public string AiProvider
    {
        get => _settingsService.Settings.AiProvider;
        set
        {
            if (_settingsService.Settings.AiProvider == value) return;
            _settingsService.Settings.AiProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiModelOptions));
            OnPropertyChanged(nameof(ShowEndpointField));
            OnPropertyChanged(nameof(AiApiKeyLabel));

            if (ProviderModels.TryGetValue(value, out var models) &&
                !models.Contains(_settingsService.Settings.AiModel))
            {
                AiModel = models[0];
            }

            _ = _settingsService.SaveAsync();

            // When switching to Gemini, refresh the model list if we already have a key.
            if (string.Equals(value, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                var key = !string.IsNullOrWhiteSpace(_aiApiKey)
                    ? _aiApiKey
                    : _settingsService.Settings.AiApiKey;

                if (!string.IsNullOrWhiteSpace(key))
                    _ = RefreshGeminiModelsAsync(key);
            }
        }
    }

    public List<string> AiModelOptions =>
        ProviderModels.TryGetValue(AiProvider, out var list)
            ? list
            : ProviderModels["Custom"];

    public bool ShowEndpointField =>
        !string.Equals(AiProvider, "Gemini", StringComparison.OrdinalIgnoreCase);

    public string AiApiKeyLabel => AiProvider switch
    {
        "Gemini" => "Gemini API Key",
        "OpenAI" => "OpenAI API Key",
        _        => "API Key"
    };

    public string AiApiKey
    {
        get => _aiApiKey;
        set => _aiApiKey = value;
    }

    /// <summary>
    /// Returns the currently persisted API key so the view can pre-fill the
    /// PasswordBox on load. Not data-bound — only read once at page init.
    /// </summary>
    public string LoadedAiApiKey => _settingsService.Settings.AiApiKey;

    public bool IsLoadingGeminiModels
    {
        get => _isLoadingGeminiModels;
        private set => SetProperty(ref _isLoadingGeminiModels, value);
    }

    public string AiSettingsStatus
    {
        get => _aiSettingsStatus;
        private set
        {
            if (SetProperty(ref _aiSettingsStatus, value))
                OnPropertyChanged(nameof(HasAiSettingsStatus));
        }
    }

    public bool HasAiSettingsStatus => !string.IsNullOrEmpty(_aiSettingsStatus);

    public string AiApiEndpoint
    {
        get => _settingsService.Settings.AiApiEndpoint;
        set
        {
            if (_settingsService.Settings.AiApiEndpoint == value) return;
            _settingsService.Settings.AiApiEndpoint = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }

    public string AiModel
    {
        get => _settingsService.Settings.AiModel;
        set
        {
            if (_settingsService.Settings.AiModel == value) return;
            _settingsService.Settings.AiModel = value;
            OnPropertyChanged();
            _ = _settingsService.SaveAsync();
        }
    }

    public async Task SaveAiSettingsAsync()
    {
        // Always write back whatever is in the field — including empty string
        // if the user deliberately cleared the key.
        _settingsService.Settings.AiApiKey = _aiApiKey;

        await _settingsService.SaveAsync();

        // After saving a Gemini key, fetch the live model list.
        if (string.Equals(AiProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var key = _settingsService.Settings.AiApiKey;
            if (!string.IsNullOrWhiteSpace(key))
                await RefreshGeminiModelsAsync(key);
        }
    }

    /// <summary>Legacy sync overload kept for call sites that haven't been updated yet.</summary>
    public void SaveAiSettings() => _ = SaveAiSettingsAsync();

    public bool IsLightTheme => ThemeService.IsLight(_settingsService);

    public async Task ToggleThemeAsync()
    {
        await ThemeService.ToggleAsync(_settingsService);
        OnPropertyChanged(nameof(IsLightTheme));
    }

    private async Task RefreshGeminiModelsAsync(string apiKey)
    {
        IsLoadingGeminiModels = true;
        AiSettingsStatus = "Fetching available Gemini models…";
        try
        {
            var models = await _aiService.GetGeminiModelsAsync(apiKey);
            if (models.Count == 0)
            {
                AiSettingsStatus = "No text-generation models found for this API key.";
                return;
            }

            ProviderModels["Gemini"] = models;
            OnPropertyChanged(nameof(AiModelOptions));

            // If the currently selected model isn't in the new list, switch to the first one.
            if (!models.Contains(_settingsService.Settings.AiModel))
                AiModel = models[0];

            AiSettingsStatus = $"Loaded {models.Count} available model(s).";
        }
        catch (Exception ex)
        {
            AiSettingsStatus = $"Could not fetch models: {ex.Message}";
        }
        finally
        {
            IsLoadingGeminiModels = false;
        }
    }
}
