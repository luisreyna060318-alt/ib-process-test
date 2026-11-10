using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.Views;

/// <summary>Reviewer feedback: Oracle IB_PROCESS_FEEDBACK, or a local CSV in demo mode.</summary>
public partial class FeedbackWindow : Window
{
    private readonly string _screen;
    private readonly UserContext _user;
    private readonly IFeedbackStore _store;

    public FeedbackWindow(string screen, UserContext user, IFeedbackStore store)
    {
        InitializeComponent();
        _screen = screen;
        _user = user;
        _store = store;
        ContextText.Text = $"Screen: {screen} · Function: {user.FunctionText} · Windows user: {user.NtUser}";
        PathText.Text = $"Saved to: {store.Location}";
        Comment.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Comment.Text))
        {
            MessageBox.Show(this, "Write a comment.", "Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            _store.Add(new FeedbackItem(_user.NtUser, _user.FunctionText, _screen,
                (Category.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "", Comment.Text.Trim(), DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Feedback not saved:\n{ex.Message}", "Feedback", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(this, "Thank you! Feedback saved.", "Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_store.Location);
        if (dir is null || !Path.IsPathRooted(_store.Location))
        {
            MessageBox.Show(this, _store.Location, "Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}
