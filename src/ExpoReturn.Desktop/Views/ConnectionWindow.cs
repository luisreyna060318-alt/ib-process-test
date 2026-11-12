using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.Views;

/// <summary>
/// Shown only when the automatic connection at start-up failed: says the database could not be reached and retries
/// three times (immediately, after 5 s, after 10 s). If every retry fails the application does not open.
/// Closing the window stops the retries and closes the application.
/// </summary>
public sealed class ConnectionWindow : Window
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _close = new() { Content = "Close", Width = 110, IsCancel = true, Visibility = Visibility.Collapsed };
    private readonly CancellationTokenSource _cancel = new();
    private readonly string _server;

    public ConnectionWindow(string server, string firstError)
    {
        _server = server;
        Title = "Expo & Return · database connection";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        if (System.Windows.Application.Current.TryFindResource("SecondaryButton") is Style s) _close.Style = s;
        _close.Click += (_, _) => Close();
        Closed += (_, _) => _cancel.Cancel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(_close);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Could not connect to the database ({server}).", FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(_error);
        panel.Children.Add(_status);
        panel.Children.Add(_help);
        panel.Children.Add(buttons);
        Content = panel;
        _error.Text = firstError;
    }

    /// <summary>Runs the retries; returns the services, or null when every retry failed or the user closed the window.</summary>
    public async Task<AppServices?> RetryAsync(Func<AppServices> connect, string firstError)
    {
        try
        {
            var outcome = await ConnectionRetry.RetryAsync(connect, firstError, info =>
            {
                _error.Text = info.LastError;
                _status.Text = info.Wait == TimeSpan.Zero
                    ? $"Retrying now… ({info.Retry} of {info.Retries})"
                    : $"Retry {info.Retry} of {info.Retries} in {info.Wait.TotalSeconds:0} s…";
            }, CountDownAsync, _cancel.Token);
            if (outcome.Connected) return outcome.Result;
            ShowFailure(outcome.LastError);
            return null;
        }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>Waits, showing the seconds left.</summary>
    private async Task CountDownAsync(TimeSpan wait, CancellationToken ct)
    {
        var prefix = _status.Text.Split(" in ")[0];
        for (var left = (int)Math.Ceiling(wait.TotalSeconds); left > 0; left--)
        {
            _status.Text = $"{prefix} in {left} s…";
            await Task.Delay(1000, ct);
        }
        _status.Text = $"{prefix}: connecting…";
    }

    private void ShowFailure(string? error)
    {
        _error.Text = error ?? "";
        _status.Text = "The application cannot open without the database: every retry failed.";
        _help.Text = "Check the network connection to the plant (SharedServersAccess reads its servers.ini from the network share) and " +
                     $"that tnsnames.ora knows {_server} (TNS_ADMIN or %ORACLE_HOME%\\network\\admin). Then open the application again.";
        _help.Visibility = Visibility.Visible;
        _close.Visibility = Visibility.Visible;
    }
}
