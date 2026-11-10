using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.Views;

/// <summary>
/// Stores reviewer feedback in a local CSV (%LOCALAPPDATA%\ExpoReturn\feedback.csv) to be consolidated later.
/// </summary>
public partial class FeedbackWindow : Window
{
    private readonly string _screen;
    private readonly UserContext _user;

    public static string FeedbackFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpoReturn", "feedback.csv");

    public FeedbackWindow(string screen, UserContext user)
    {
        InitializeComponent();
        _screen = screen;
        _user = user;
        ContextText.Text = $"Screen: {screen} · Function: {user.FunctionText} · Windows user: {user.NtUser}";
        PathText.Text = $"Saved to: {FeedbackFile}";
        Comment.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Comment.Text))
        {
            MessageBox.Show(this, "Write a comment.", "Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FeedbackFile)!);
        var isNew = !File.Exists(FeedbackFile);
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var line = string.Join(",", Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm")), Q(_user.NtUser), Q(_user.FunctionText),
            Q(_screen), Q((Category.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ""), Q(Comment.Text.Trim()));
        var sb = new StringBuilder();
        if (isNew) sb.AppendLine("Date,WindowsUser,Function,Screen,Type,Comment");
        sb.AppendLine(line);
        File.AppendAllText(FeedbackFile, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show(this, "Thank you! Feedback saved.", "Feedback", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(FeedbackFile)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}
