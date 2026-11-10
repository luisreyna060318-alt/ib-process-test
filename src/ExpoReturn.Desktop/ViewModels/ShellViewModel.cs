using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using Microsoft.Win32;

namespace ExpoReturn.Desktop.ViewModels;

public sealed record NavItem(string Key, string Title, string Glyph);

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly string _ntUser;

    public ShellViewModel(AppServices services, string ntUser)
    {
        _services = services;
        _ntUser = ntUser;
        NavItems = new[]
        {
            new NavItem("list", "Returns inbox", "\uE8FD"),
            new NavItem("new", "New return", "\uE710"),
            new NavItem("dashboard", "Balance / Duties", "\uE9D2"),
            new NavItem("alerts", "Alerts", "\uE7BA"),
            new NavItem("users", "Users & Access", "\uE716"),
            new NavItem("audit", "Audit log", "\uE81C"),
        };
        _realUser = services.Users.ContextFor(ntUser);
        _currentUser = _realUser;
        BuildViewAs();
        _selectedNav = NavItems[0];
        _currentPage = new PalletListViewModel(services, _currentUser, OpenPallet);
        _dataSourceLabel = services.DataSourceLabel;
        services.Users.Changed += (_, _) => RefreshIdentity();
    }

    public IReadOnlyList<NavItem> NavItems { get; }
    public string AppTitle => "Expo & Return FG Follow Up";
    public string DraftNotice => "DRAFT FOR REVIEW · no database yet: pallet changes are lost when the app closes";

    /// <summary>The Windows user running the app.</summary>
    [ObservableProperty] private UserContext _realUser;
    public bool IsOwner => RealUser.IsOwner;
    public string IdentityText => $"{RealUser.DisplayName} ({RealUser.NtUser})";
    public string AccessText => RealUser.IsOwner ? "Owner · full access"
        : $"{RealUser.DepartmentCode ?? "No department"} · {RealUser.ValueStreamText} · {RealUser.FunctionText}";
    public string PendingRequestsText => IsOwner && _services.Users.Requests().Count is var n and > 0 ? $"{n} pending request(s)" : "";

    /// <summary>Owner only: preview the application as another user (test functions without their PC).</summary>
    [ObservableProperty] private ICollectionView _viewAsOptions = null!;

    partial void OnRealUserChanged(UserContext value)
    {
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(IdentityText));
        OnPropertyChanged(nameof(AccessText));
        OnPropertyChanged(nameof(PendingRequestsText));
    }

    private void BuildViewAs()
    {
        var list = new List<UserContext> { RealUser };
        list.AddRange(_services.Users.Users().Where(u => u.NtUser != RealUser.NtUser).Select(u => _services.Users.ContextFor(u.NtUser)));
        var view = new ListCollectionView(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(UserContext.Group)));
        ViewAsOptions = view;
    }

    private void RefreshIdentity()
    {
        var viewing = CurrentUser.NtUser;
        RealUser = _services.Users.ContextFor(_ntUser);
        BuildViewAs();
        CurrentUser = IsOwner && viewing != RealUser.NtUser
            ? ViewAsOptions.Cast<UserContext>().FirstOrDefault(u => u.NtUser == viewing) ?? RealUser
            : RealUser;
    }

    private UserContext _currentUser;
    /// <summary>
    /// Effective user: the Windows user, or the user the Owner is previewing. The ComboBox can send null while its
    /// list is replaced: ignored, the matching user of the new list is assigned right after.
    /// </summary>
    public UserContext CurrentUser
    {
        get => _currentUser;
        set
        {
            if (value is null || ReferenceEquals(value, _currentUser)) return;
            if (!RealUser.IsOwner && value.NtUser != RealUser.NtUser) return;
            _currentUser = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPreviewing));
            CurrentPage.User = value;
        }
    }
    public bool IsPreviewing => CurrentUser.NtUser != RealUser.NtUser;

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private string _dataSourceLabel;

    [RelayCommand]
    private void EditProfile()
    {
        var win = new Views.ProfileWindow
        {
            DataContext = new ProfileViewModel(_services, _ntUser, firstRun: false),
            Owner = System.Windows.Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        win.ShowDialog();
        RefreshIdentity();
    }

    private bool _suppressNav;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null || _suppressNav) return;
        if (!ConfirmLeave()) return;
        CurrentPage = value.Key switch
        {
            "new" => new PalletDetailViewModel(_services, CurrentUser, null, Back),
            "dashboard" => new DashboardViewModel(_services, CurrentUser, OpenList),
            "alerts" => new AlertsViewModel(_services, CurrentUser, OpenPallet),
            "users" => new UsersViewModel(_services, CurrentUser, RealUser),
            "audit" => new AuditViewModel(_services, CurrentUser),
            _ => new PalletListViewModel(_services, CurrentUser, OpenPallet),
        };
    }

    private bool ConfirmLeave()
    {
        if (CurrentPage is PalletDetailViewModel { IsDirty: true })
            return MessageBox.Show("There are unsaved changes. Discard them?", "Unsaved changes",
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

    /// <summary>UX-B13: opens the inbox with a filter (from Balance / Duties).</summary>
    private void OpenList(ListFilter filter)
    {
        if (!ConfirmLeave()) return;
        _suppressNav = true;
        SelectedNav = NavItems[0];
        _suppressNav = false;
        CurrentPage = new PalletListViewModel(_services, CurrentUser, OpenPallet, filter);
    }

    /// <summary>UX-B12: Ctrl+N opens a new return from any screen.</summary>
    [RelayCommand]
    private void NewReturn()
    {
        if (!ConfirmLeave()) return;
        _suppressNav = true;
        SelectedNav = NavItems[1];
        _suppressNav = false;
        CurrentPage = new PalletDetailViewModel(_services, CurrentUser, null, Back);
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
            Title = "Select the MATRIZ DE RASTREO 2.0 (.xlsx)",
            Filter = "Excel (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Mouse.SetWait();
            var result = _services.ImportLegacy(dlg.FileName);
            DataSourceLabel = _services.DataSourceLabel;
            Back();
            var top = string.Join("\n", result.IssueCounts.OrderByDescending(k => k.Value).Take(8).Select(k => $"  • {k.Key}: {k.Value}"));
            MessageBox.Show($"{result.Summary}\n\nMain findings:\n{top}\n\nHistorical records are READ-ONLY and nothing is written to the Excel file.",
                "Excel 2.0 import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The file could not be read:\n{ex.Message}\n\nIf it is open in Excel, close it and try again.",
                "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Mouse.Clear(); }
    }

    [RelayCommand]
    private void ResetDemo()
    {
        if (!ConfirmLeave()) return;
        _services.ResetDemo();
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
