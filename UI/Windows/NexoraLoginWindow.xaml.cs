using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        var app = (App)Application.Current;
        var nexoraService = app.ServiceProvider?.GetService<INexoraAccountService>();
        _nexoraService = nexoraService!;

        _viewModel = new NexoraLoginViewModel(nexoraService!, () => PasswordBox.Password);
        DataContext = _viewModel;

        // PasswordBox kan niet binden via MVVM, dus we updaten CanExecute handmatig
        // zodra de gebruiker iets typt → knop wordt actief.
        PasswordBox.PasswordChanged += (s, e) =>
            _viewModel.LoginCommand.RaiseCanExecuteChanged();

        _viewModel.RequestClose += (s, e) =>
        {
            LoginSuccessful = true;
            Close();
        };

        _viewModel.TwoFactorRequested += (s, e) =>
        {
            // Schakel over van het inlogformulier naar de 2FA-code view.
            ChoiceView.Visibility    = Visibility.Collapsed;
            LoginFormView.Visibility = Visibility.Collapsed;
            TwoFactorView.Visibility = Visibility.Visible;
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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Als de gebruiker het venster sluit zonder in te loggen → app afsluiten.
        if (!LoginSuccessful && !ContinuedWithoutAccount && _nexoraService.Current == null)
            Application.Current.Shutdown();
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
        ChoiceView.Visibility    = Visibility.Collapsed;
        TwoFactorView.Visibility = Visibility.Collapsed;
        LoginFormView.Visibility = Visibility.Visible;
        EmailOrUsernameBox.Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        LoginFormView.Visibility = Visibility.Collapsed;
        TwoFactorView.Visibility = Visibility.Collapsed;
        ChoiceView.Visibility    = Visibility.Visible;
    }

    private void ContinueWithoutAccount_Click(object sender, RoutedEventArgs e)
    {
        ContinuedWithoutAccount = true;
        Close();
    }
}
