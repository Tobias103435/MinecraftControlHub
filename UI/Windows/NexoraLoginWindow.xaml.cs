using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using MinecraftControlHub.Core.Services;
using MinecraftControlHub.UI.ViewModels;
using System.ComponentModel;

namespace MinecraftControlHub.UI.Windows;

public partial class NexoraLoginWindow : Window
{
    private readonly NexoraLoginViewModel _viewModel;
    private readonly INexoraAccountService _nexoraService;

    public NexoraLoginWindow()
    {
        InitializeComponent();

        var app = (App)Application.Current!;
        var nexoraService = app.ServiceProvider?.GetService<INexoraAccountService>();
        _nexoraService = nexoraService!;

        _viewModel = new NexoraLoginViewModel(nexoraService!, () => PasswordBox.Text ?? string.Empty);
        DataContext = _viewModel;

        // PasswordBox kan niet binden via MVVM, dus we updaten CanExecute handmatig
        // zodra de gebruiker iets typt → knop wordt actief.
        PasswordBox.TextChanged += (s, e) =>
            _viewModel.LoginCommand.RaiseCanExecuteChanged();

        _viewModel.RequestClose += (s, e) =>
        {
            LoginSuccessful = true;
            Close();
        };

        _viewModel.TwoFactorRequested += (s, e) =>
        {
            // Schakel over van het inlogformulier naar de 2FA-code view.
            ChoiceView.IsVisible    = false;
            LoginFormView.IsVisible = false;
            TwoFactorView.IsVisible = true;
            TwoFactorCodeBox.Focus();
        };

        // Enter in EmailBox → focus naar PasswordBox
        EmailOrUsernameBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
                PasswordBox.Focus();
        };

        // Enter in PasswordBox → login uitvoeren
        PasswordBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && _viewModel.LoginCommand.CanExecute(null))
                _viewModel.LoginCommand.Execute(null);
        };

        // Enter in 2FA code box → verify uitvoeren
        TwoFactorCodeBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && _viewModel.VerifyCommand.CanExecute(null))
                _viewModel.VerifyCommand.Execute(null);
        };
    }

    public bool LoginSuccessful { get; private set; }

    public bool ContinuedWithoutAccount { get; private set; }

    private void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        // Als de gebruiker het venster sluit zonder in te loggen → app afsluiten.
        if (!LoginSuccessful && !ContinuedWithoutAccount && _nexoraService.Current == null)
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void SignUp_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://nexoragames.nl",
            UseShellExecute = true
        });
    }

    private void SignInCard_Click(object sender, RoutedEventArgs e)
    {
        ChoiceView.IsVisible    = false;
        TwoFactorView.IsVisible = false;
        LoginFormView.IsVisible = true;
        EmailOrUsernameBox.Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        LoginFormView.IsVisible = false;
        TwoFactorView.IsVisible = false;
        ChoiceView.IsVisible    = true;
    }

    private void ContinueWithoutAccount_Click(object sender, RoutedEventArgs e)
    {
        ContinuedWithoutAccount = true;
        Close();
    }
}
