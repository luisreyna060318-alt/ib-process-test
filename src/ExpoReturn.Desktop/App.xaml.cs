using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ExpoReturn.Desktop.ViewModels;
using ExpoReturn.Infrastructure.Oracle;

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
            var ex = args.Exception is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : args.Exception;
            MessageBox.Show(ex.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var config = AppConfig.Load();
        var services = config.UsesOracle ? ConnectOracle(config) : AppServices.CreateLocal(config);
        if (services is null) { Shutdown(); return; }
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

    /// <summary>
    /// Master key: environment variable (SHARED_SERVERS_CLAVE by default) or asked up to 3 times. Then the account comes
    /// from SharedServersAccess.dll and the IB_PROCESS_ tables are checked. If it fails, the user may continue in Local
    /// (demo) mode, where nothing reaches the database.
    /// </summary>
    private static AppServices? ConnectOracle(AppConfig config)
    {
        var credentials = new SharedServersCredentials(config.Oracle);
        var fixedKey = credentials.KeyFromEnvironment;
        string? error = null;
        for (var attempt = 1; attempt <= (fixedKey is null ? 3 : 1); attempt++)
        {
            var key = fixedKey ?? Views.MasterKeyDialog.Ask(config.Oracle.Server, error);
            if (key is null) break;
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                credentials.Initialize(key);
                return AppServices.CreateOracle(config, credentials);
            }
            catch (CredentialsException ex) { error = ex.Message; }
            catch (Exception ex)
            {
                error = $"Connected to SharedServersAccess but Oracle failed: {(ex is System.Reflection.TargetInvocationException t ? t.InnerException?.Message : ex.Message)}";
                break;      // the key was right: asking it again does not help
            }
            finally { System.Windows.Input.Mouse.OverrideCursor = null; }
        }
        var answer = MessageBox.Show(
            $"The database could not be opened.\n\n{error ?? "Cancelled."}\n\n" +
            "Check: SharedServersAccess.dll next to the application, the master key, tnsnames.ora (TnsAdmin in appsettings.json) " +
            "and that the IB_PROCESS_ tables exist.\n\nContinue in Local mode (demo data, nothing is saved to the database)?",
            "Expo & Return · Oracle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes ? AppServices.CreateLocal(config) : null;
    }
}
