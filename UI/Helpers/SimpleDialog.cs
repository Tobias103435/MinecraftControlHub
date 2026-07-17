using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinecraftControlHub.UI.Helpers;

/// <summary>
/// Minimal modal Yes/No and OK dialogs standing in for WPF's System.Windows.MessageBox,
/// which does not exist in Avalonia. Not a full replacement (no icons), just enough to
/// preserve the original confirm/notify behavior across the ported pages/windows.
/// </summary>
public static class SimpleDialog
{
    public static async Task<bool> ConfirmAsync(Window? owner, string message, string title)
    {
        if (owner == null) return false;

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var window = BuildWindow(title, message, buttonsPanel);

        var no  = new Button { Content = "No", MinWidth = 80 };
        var yes = new Button { Content = "Yes", MinWidth = 80 };
        buttonsPanel.Children.Add(no);
        buttonsPanel.Children.Add(yes);

        yes.Click += (_, _) => window.Close(true);
        no.Click  += (_, _) => window.Close(false);

        return await window.ShowDialog<bool>(owner);
    }

    public static async Task InfoAsync(Window? owner, string message, string title)
    {
        if (owner == null) return;

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var window = BuildWindow(title, message, buttonsPanel);

        var ok = new Button { Content = "OK", MinWidth = 80 };
        buttonsPanel.Children.Add(ok);
        ok.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
    }

    private static Window BuildWindow(string title, string message, StackPanel buttonsPanel)
    {
        return new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    buttonsPanel
                }
            }
        };
    }
}
