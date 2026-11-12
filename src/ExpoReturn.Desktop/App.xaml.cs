using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ExpoReturn.Desktop.ViewModels;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // English UI with plant formats: dates dd/MM/yyyy, '.' decimal separator.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;          // the connection runs on a background thread
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            var ex = args.Exception is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : args.Exception;
            MessageBox.Show(ex.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // The database connection is mandatory and automatic (no key to type, no local mode).
        var config = AppConfig.Load();
        var first = await ConnectionRetry.FirstAttemptAsync(() => AppServices.Connect(config));
        var services = first.Result;
        if (services is null)
        {
            var retryWindow = new Views.ConnectionWindow(config.Oracle.Server, first.LastError ?? "");
            retryWindow.Show();
            services = await retryWindow.RetryAsync(() => AppServices.Connect(config), first.LastError ?? "");
            if (services is null)
            {
                if (retryWindow.IsVisible) { retryWindow.Closed += (_, _) => Shutdown(); return; }   // the user reads the message and closes it
                Shutdown();
                return;
            }
            retryWindow.Close();
        }
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
