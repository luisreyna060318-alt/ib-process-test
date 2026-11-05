using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ExpoReturn.Desktop.ViewModels;

namespace ExpoReturn.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Formatos MX (fechas dd/MM/yyyy, separador decimal '.').
        var culture = new CultureInfo("es-MX");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Error inesperado (borrador)", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var services = AppServices.CreateDemo();
        var window = new MainWindow { DataContext = new ShellViewModel(services) };
        window.Show();
    }
}
