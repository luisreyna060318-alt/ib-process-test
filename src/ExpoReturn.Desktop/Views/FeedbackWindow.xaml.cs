using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.Views;

/// <summary>
/// Captura comentarios de los revisores del borrador en un CSV local
/// (%LOCALAPPDATA%\ExpoReturnBorrador\comentarios.csv) para consolidarlos después.
/// </summary>
public partial class FeedbackWindow : Window
{
    private readonly string _screen;
    private readonly UserContext _user;

    public static string FeedbackFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExpoReturnBorrador", "comentarios.csv");

    public FeedbackWindow(string screen, UserContext user)
    {
        InitializeComponent();
        _screen = screen;
        _user = user;
        ContextText.Text = $"Pantalla: {screen} · Rol simulado: {user.Role.DisplayName()} · Usuario Windows: {Environment.UserName}";
        PathText.Text = $"Se guarda en: {FeedbackFile}";
        Comment.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Comment.Text))
        {
            MessageBox.Show(this, "Escriba un comentario.", "Comentario", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FeedbackFile)!);
        var isNew = !File.Exists(FeedbackFile);
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var line = string.Join(",", Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm")), Q(Environment.UserName), Q(_user.Role.DisplayName()),
            Q(_screen), Q((Category.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ""), Q(Comment.Text.Trim()));
        var sb = new StringBuilder();
        if (isNew) sb.AppendLine("Fecha,UsuarioWindows,RolSimulado,Pantalla,Tipo,Comentario");
        sb.AppendLine(line);
        File.AppendAllText(FeedbackFile, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show(this, "¡Gracias! Comentario guardado.", "Comentario", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(FeedbackFile)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}
