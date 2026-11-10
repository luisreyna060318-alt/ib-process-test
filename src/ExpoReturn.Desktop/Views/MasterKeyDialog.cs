using System.Windows;
using System.Windows.Controls;

namespace ExpoReturn.Desktop.Views;

/// <summary>
/// Asks for the SharedServersAccess master key (only when the environment variable is not set). The key stays in memory
/// for this session; it is never written to disk or to the configuration.
/// </summary>
public static class MasterKeyDialog
{
    public static string? Ask(string server, string? error)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 8), Height = 28 };
        var ok = new Button { Content = "Connect", IsDefault = true, Width = 110, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 110 };
        if (System.Windows.Application.Current.TryFindResource("PrimaryButton") is Style p) ok.Style = p;
        if (System.Windows.Application.Current.TryFindResource("SecondaryButton") is Style s) cancel.Style = s;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Master key of SharedServersAccess to connect to the database ({server}).\n" +
                   "It is used only for this session and is not saved.",
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(error))
            panel.Children.Add(new TextBlock { Text = error, Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = "Connect to Oracle", Content = panel, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        ok.Click += (_, _) => { if (box.Password.Length > 0) win.DialogResult = true; else box.Focus(); };
        win.Loaded += (_, _) => box.Focus();
        return win.ShowDialog() == true ? box.Password : null;
    }
}
