using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using Microsoft.Win32;

namespace ExpoReturn.Desktop.ViewModels;

public sealed record NavItem(string Key, string Title, string Glyph);

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ShellViewModel(AppServices services)
    {
        _services = services;
        Users = services.Champions.DemoUsers();
        _currentUser = Users[0];
        NavItems = new[]
        {
            new NavItem("list", "Bandeja de retornos", ""),
            new NavItem("new", "Nuevo retorno", ""),
            new NavItem("dashboard", "Balance / Duties", ""),
            new NavItem("alerts", "Alertas", ""),
            new NavItem("champions", "Champions VS", ""),
            new NavItem("audit", "Bitácora", ""),
        };
        _selectedNav = NavItems[0];
        _currentPage = new PalletListViewModel(services, _currentUser, OpenPallet);
    }

    public IReadOnlyList<NavItem> NavItems { get; }
    public string AppTitle => "Expo & Return FG Follow Up";
    public string DraftNotice => "BORRADOR PARA REVISIÓN · sin base de datos: los cambios se pierden al cerrar";

    [ObservableProperty] private IReadOnlyList<UserContext> _users;
    [ObservableProperty] private UserContext _currentUser;
    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private string _dataSourceLabel = "Datos de demostración (sintéticos)";

    partial void OnCurrentUserChanged(UserContext value) => CurrentPage.User = value;

    private bool _suppressNav;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null || _suppressNav) return;
        if (!ConfirmLeave()) return;
        CurrentPage = value.Key switch
        {
            "new" => new PalletDetailViewModel(_services, CurrentUser, null, Back),
            "dashboard" => new DashboardViewModel(_services, CurrentUser, OpenPallet),
            "alerts" => new AlertsViewModel(_services, CurrentUser, OpenPallet),
            "champions" => new ChampionsViewModel(_services, CurrentUser),
            "audit" => new AuditViewModel(_services, CurrentUser),
            _ => new PalletListViewModel(_services, CurrentUser, OpenPallet),
        };
    }

    private bool ConfirmLeave()
    {
        if (CurrentPage is PalletDetailViewModel { IsDirty: true })
            return MessageBox.Show("Hay cambios sin guardar. ¿Descartarlos?", "Cambios sin guardar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        return true;
    }

    private void OpenPallet(int id)
    {
        if (!ConfirmLeave()) return;
        var pallet = _services.Repository.Get(id);
        if (pallet is null) return;
        CurrentPage = new PalletDetailViewModel(_services, CurrentUser, pallet, Back);
    }

    private void Back()
    {
        _suppressNav = true;
        SelectedNav = NavItems[0];
        _suppressNav = false;
        CurrentPage = new PalletListViewModel(_services, CurrentUser, OpenPallet);
    }

    [RelayCommand]
    private void ImportExcel()
    {
        if (!ConfirmLeave()) return;
        var dlg = new OpenFileDialog
        {
            Title = "Seleccione la MATRIZ DE RASTREO 2.0 (.xlsx)",
            Filter = "Excel (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Mouse.SetWait();
            var result = _services.ImportLegacy(dlg.FileName);
            Users = _services.Champions.DemoUsers();
            CurrentUser = Users[0];
            DataSourceLabel = _services.DataSourceLabel;
            Back();
            var top = string.Join("\n", result.IssueCounts.OrderByDescending(k => k.Value).Take(8).Select(k => $"  • {k.Key}: {k.Value}"));
            MessageBox.Show($"{result.Summary}\n\nPrincipales observaciones:\n{top}\n\nLos registros históricos son de SÓLO LECTURA y nada se guarda en el Excel.",
                "Importación del Excel 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo leer el archivo:\n{ex.Message}\n\nSi está abierto en Excel, ciérrelo e intente de nuevo.",
                "Importación", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Mouse.Clear(); }
    }

    [RelayCommand]
    private void ResetDemo()
    {
        if (!ConfirmLeave()) return;
        _services.ResetDemo();
        Users = _services.Champions.DemoUsers();
        CurrentUser = Users[0];
        DataSourceLabel = _services.DataSourceLabel;
        Back();
    }

    [RelayCommand]
    private void OpenFeedback()
    {
        var win = new Views.FeedbackWindow(CurrentPage.Title, CurrentUser) { Owner = System.Windows.Application.Current.MainWindow };
        win.ShowDialog();
    }

    private static class Mouse
    {
        public static void SetWait() => System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        public static void Clear() => System.Windows.Input.Mouse.OverrideCursor = null;
    }
}
