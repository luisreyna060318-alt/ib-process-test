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

        // English UI with plant formats: dates dd/MM/yyyy, '.' decimal separator.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Unexpected error (draft)", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var config = AppConfig.Load();
        var services = AppServices.Create(config);
        var ntUser = AppConfig.CurrentWindowsUser;

        // First run: the user registers department and Value Stream(s); the function starts as Viewer.
        if (services.Users.Find(ntUser) is null)
        {
            var profile = new Views.ProfileWindow { DataContext = new ProfileViewModel(services, ntUser, firstRun: true) };
            if (profile.ShowDialog() != true) { Shutdown(); return; }
        }

        var window = new MainWindow { DataContext = new ShellViewModel(services, ntUser) };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}
