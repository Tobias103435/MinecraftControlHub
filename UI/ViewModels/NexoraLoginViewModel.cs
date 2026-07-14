using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MinecraftControlHub.Core.Services;

namespace MinecraftControlHub.UI.ViewModels;

public class NexoraLoginViewModel : INotifyPropertyChanged
{
    private readonly INexoraAccountService _nexoraService;
    private readonly Func<string> _passwordProvider;
    private string _emailOrUsername = string.Empty;
    private string _error = string.Empty;
    private bool _isLoggingIn;
    private bool _is2FARequired;
    private string _twoFactorCode = string.Empty;
    private string _twoFactorPrompt = string.Empty;
    private string _challenge = string.Empty;
    
    public string EmailOrUsername
    {
        get => _emailOrUsername;
        set
        {
            _emailOrUsername = value;
            OnPropertyChanged();
            ClearError();
        }
    }
    
    public string Error
    {
        get => _error;
        set
        {
            _error = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }
    
    public bool HasError => !string.IsNullOrEmpty(_error);
    
    public bool IsLoggingIn
    {
        get => _isLoggingIn;
        set
        {
            _isLoggingIn = value;
            OnPropertyChanged();
            LoginCommand?.RaiseCanExecuteChanged();
            VerifyCommand?.RaiseCanExecuteChanged();
        }
    }
    
    /// <summary>True once the server has asked for a second-factor code.</summary>
    public bool Is2FARequired
    {
        get => _is2FARequired;
        set
        {
            _is2FARequired = value;
            OnPropertyChanged();
        }
    }
    
    /// <summary>The verification code the user typed in.</summary>
    public string TwoFactorCode
    {
        get => _twoFactorCode;
        set
        {
            _twoFactorCode = value;
            OnPropertyChanged();
            ClearError();
            VerifyCommand?.RaiseCanExecuteChanged();
        }
    }
    
    /// <summary>Human-readable hint telling the user where the code comes from.</summary>
    public string TwoFactorPrompt
    {
        get => _twoFactorPrompt;
        set
        {
            _twoFactorPrompt = value;
            OnPropertyChanged();
        }
    }
    
    public RelayCommand LoginCommand { get; }
    public RelayCommand VerifyCommand { get; }
    public RelayCommand CancelCommand { get; }
    
    public event EventHandler? RequestClose;
    
    /// <summary>Raised when the login needs a second-factor code, so the view can switch.</summary>
    public event EventHandler? TwoFactorRequested;
    
    public NexoraLoginViewModel(INexoraAccountService nexoraService, Func<string> passwordProvider)
    {
        _nexoraService = nexoraService;
        _passwordProvider = passwordProvider;
        
        LoginCommand = new RelayCommand(async () => await LoginAsync(), CanLogin);
        VerifyCommand = new RelayCommand(async () => await VerifyAsync(), CanVerify);
        CancelCommand = new RelayCommand(Cancel);
    }
    
    private bool CanLogin()
    {
        return !string.IsNullOrWhiteSpace(EmailOrUsername) && 
               !string.IsNullOrWhiteSpace(_passwordProvider()) && 
               !IsLoggingIn;
    }
    
    private async Task LoginAsync()
    {
        IsLoggingIn = true;
        ClearError();
        
        try
        {
            var password = _passwordProvider();
            var response = await _nexoraService.SignInAsync(EmailOrUsername, password);
            
            if (response.Success && response.Data != null && response.Data.TwoFactorRequired)
            {
                // A second factor is needed before a token is issued.
                _challenge = response.Data.Challenge ?? string.Empty;
                TwoFactorPrompt = response.Data.Method == "totp"
                    ? "Enter the 6-digit code from your authenticator app."
                    : "We emailed you a 6-digit code. Enter it below to finish signing in.";
                TwoFactorCode = string.Empty;
                Is2FARequired = true;
                TwoFactorRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (response.Success)
            {
                // Login successful
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Error = response.Error ?? "Login failed";
            }
        }
        catch (Exception ex)
        {
            Error = $"An error occurred: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
    
    private bool CanVerify()
    {
        return !string.IsNullOrWhiteSpace(TwoFactorCode) && !IsLoggingIn;
    }
    
    private async Task VerifyAsync()
    {
        IsLoggingIn = true;
        ClearError();
        
        try
        {
            var response = await _nexoraService.Verify2FAAsync(_challenge, TwoFactorCode.Trim());
            
            if (response.Success)
            {
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Error = response.Error ?? "Verification failed";
            }
        }
        catch (Exception ex)
        {
            Error = $"An error occurred: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
    
    private void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
    
    private void ClearError()
    {
        Error = string.Empty;
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?>? _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Func<object?, Task>? _executeAsync;
    
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = _ => execute();
        _canExecute = canExecute != null ? _ => canExecute() : null;
    }
    
    public RelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }
    
    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = _ => executeAsync();
        _canExecute = canExecute != null ? _ => canExecute() : null;
    }
    
    public event EventHandler? CanExecuteChanged;
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    
    public void Execute(object? parameter)
    {
        if (_executeAsync != null)
        {
            // Fire-and-forget op de WPF dispatcher zodat exceptions niet stil verdwijnen.
            var task = _executeAsync(parameter);
            task.ContinueWith(t =>
            {
                if (t.Exception != null)
                    System.Windows.MessageBox.Show(
                        $"An error occurred:\n\n{t.Exception.InnerException?.Message ?? t.Exception.Message}",
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }
        else if (_execute != null)
        {
            _execute(parameter);
        }
    }
    
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
