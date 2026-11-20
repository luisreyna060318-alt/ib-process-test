using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop.ViewModels;

public sealed record BarItem(string Label, decimal Value, double Width, string ValueText, int? ValueStreamId = null, WorkflowStatus? Status = null);

// =====================================================================
// Balance / Duties
// =====================================================================
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<ListFilter> _openList;

    public DashboardViewModel(AppServices services, UserContext user, Action<ListFilter> openList) : base(services, user)
    {
        _openList = openList;
        YearOptions = new[] { new LookupItem(null, "All years") }
            .Concat(services.Reporting.FiscalYears().Select(y => new LookupItem(y, $"Fiscal year {y} (by crossing date)"))).ToList();
        _selectedYear = YearOptions[0];
        Refresh();
    }

    public override string Title => "Balance / Duties";
    public IReadOnlyList<LookupItem> YearOptions { get; }
    [ObservableProperty] private LookupItem _selectedYear;
    [ObservableProperty] private IReadOnlyList<BalanceRow> _rows = Array.Empty<BalanceRow>();
    [ObservableProperty] private IReadOnlyList<BarItem> _balanceBars = Array.Empty<BarItem>();
    [ObservableProperty] private IReadOnlyList<BarItem> _statusBars = Array.Empty<BarItem>();
    [ObservableProperty] private BalanceRow? _total;
    [ObservableProperty] private BalanceRow? _selectedRow;

    partial void OnSelectedYearChanged(LookupItem value) => Refresh();

    public override void Refresh()
    {
        var rows = Services.Reporting.BalanceByValueStream(SelectedYear?.Value as int?);
        Rows = rows;
        Total = rows.LastOrDefault(r => r.ValueStream == "Total");
        var vsRows = rows.Where(r => r.ValueStream != "Total").ToList();
        var max = vsRows.Select(r => r.BalanceGeneralUsd).DefaultIfEmpty(0).Max();
        BalanceBars = vsRows.Select(r => new BarItem(r.ValueStream, r.BalanceGeneralUsd,
            max == 0 ? 0 : (double)(r.BalanceGeneralUsd / max) * 420, $"USD {r.BalanceGeneralUsd:N0}",
            int.Parse(r.ValueStream[2..]))).ToList();
        var dist = Services.Reporting.StatusDistribution();
        var maxCount = dist.Select(d => d.Count).DefaultIfEmpty(0).Max();
        StatusBars = dist.Select(d => new BarItem(d.Name, d.Count, maxCount == 0 ? 0 : 300.0 * d.Count / maxCount,
            d.Count.ToString(), null, d.Status)).ToList();
    }

    [RelayCommand]
    private void OpenBar(BarItem? bar)
    {
        if (bar is null) return;
        _openList(bar.Status is { } s ? new ListFilter(null, s) : new ListFilter(bar.ValueStreamId, null, OnlyOpen: true));
    }

    [RelayCommand]
    private void OpenRow(BalanceRow? row)
    {
        row ??= SelectedRow;
        if (row is null) return;
        _openList(row.ValueStream == "Total" ? new ListFilter(null, null, OnlyOpen: true)
                                             : new ListFilter(int.Parse(row.ValueStream[2..]), null, OnlyOpen: true));
    }
}

// =====================================================================
// Alerts
// =====================================================================
public sealed class AlertRow
{
    public AlertRow(Alert alert, AlertAcknowledgement? ack) { Alert = alert; Ack = ack; }
    public Alert Alert { get; }
    public AlertAcknowledgement? Ack { get; }
    public Severity Severity => Alert.Severity;
    public string SeverityText => Alert.SeverityText;
    public string TypeText => Alert.TypeText;
    public string FolioCode => Alert.FolioCode;
    public string ValueStream => Alert.ValueStream;
    public string Message => Alert.Message;
    public string Owner => Alert.Owner;
    public int PalletId => Alert.PalletId;
    public bool IsAcknowledged => Ack is not null;
    public string AckText => Ack is null ? "" : $"Handled by {Ack.NtUser} on {Ack.AtUtc.ToLocalTime():dd/MM HH:mm}: {Ack.Comment}";
}

public sealed partial class AlertsViewModel : PageViewModel
{
    /// <summary>Alerts are calculated by the database and read one page at a time (50), like the inbox.</summary>
    public const int PageSize = 50;
    private readonly Action<int> _open;
    private readonly ObservableCollection<AlertRow> _rows = new();
    private bool _loading;

    public AlertsViewModel(AppServices services, UserContext user, Action<int> open) : base(services, user)
    {
        _open = open;
        Alerts = CollectionViewSource.GetDefaultView(_rows);
        Alerts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AlertRow.Owner)));   // groups of the page
        AreaOptions = new[] { new LookupItem(null, "All areas") }.Concat(AlertFilter.Areas.Select(a => new LookupItem(a, a))).ToList();
        _selectedArea = AreaOptions[0];
        Refresh();
    }

    public override string Title => "Alerts";
    public ICollectionView Alerts { get; }
    public ObservableCollection<PageButton> PageButtons { get; } = new();
    public IReadOnlyList<LookupItem> AreaOptions { get; }
    [ObservableProperty] private LookupItem _selectedArea;
    [ObservableProperty] private int _critical;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _acknowledged;
    [ObservableProperty] private bool _showAcknowledged;
    [ObservableProperty] private int _shownCount;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _pageSummary = "";
    [ObservableProperty] private string? _message;
    public bool HasSeveralPages => PageCount > 1;
    public bool HasRecords => ShownCount > 0;

    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(HasSeveralPages));
    partial void OnShownCountChanged(int value) => OnPropertyChanged(nameof(HasRecords));
    partial void OnShowAcknowledgedChanged(bool value) => Apply();
    partial void OnSelectedAreaChanged(LookupItem value) => Apply();

    [RelayCommand] private void FirstPage() => GoToPage(1);
    [RelayCommand] private void PreviousPage() => GoToPage(Page - 1);
    [RelayCommand] private void NextPage() => GoToPage(Page + 1);
    [RelayCommand] private void LastPage() => GoToPage(PageCount);
    [RelayCommand] private void GoToPage(int? page)
    {
        if (page is not { } target) return;                        // the "…" of the pager
        Page = Math.Clamp(target, 1, Math.Max(1, PageCount));
        ShowPage();
    }

    private AlertFilter Filter => new(ShowAcknowledged, SelectedArea?.Value as string);
    private bool Handled(int palletId, AlertType type) => Services.AlertAcks.Find(palletId, type) is not null;

    /// <summary>Reload: handled alerts saved by others, the indicators and the current page again.</summary>
    public override void Refresh()
    {
        Message = null;
        if (!Try(() => Services.AlertAcks.Refresh())) return;
        Apply(keepPage: true);
    }

    /// <summary>The indicators follow the filters (one query) and the first page is read again.</summary>
    private void Apply(bool keepPage = false)
    {
        if (_loading) return;
        if (!keepPage) Page = 1;
        if (!Try(() =>
            {
                var t = Services.Reporting.AlertTotals(Filter, Handled);
                Critical = t.Critical;
                Warnings = t.Warnings;
                Acknowledged = t.Handled;
                ShownCount = t.Shown;
            })) return;
        ShowPage();
    }

    private void ShowPage()
    {
        PageCount = Math.Max(1, (int)Math.Ceiling(ShownCount / (double)PageSize));
        if (Page > PageCount) Page = PageCount;
        _rows.Clear();
        PageButtons.Clear();
        var first = (Page - 1) * PageSize;
        if (ShownCount > 0 && !Try(() =>
            {
                foreach (var a in Services.Reporting.AlertPage(Filter, Handled, first, PageSize))
                    _rows.Add(new AlertRow(a, Services.AlertAcks.Find(a.PalletId, a.Type)));
            })) return;
        PageSummary = ShownCount == 0 ? "No alerts" : $"Alerts {first + 1:N0}–{first + _rows.Count:N0} of {ShownCount:N0}";
        foreach (var b in PageButton.Build(Page, PageCount)) PageButtons.Add(b);
    }

    /// <summary>A query that fails (network, database) shows the reason instead of closing the screen.</summary>
    private bool Try(Action query)
    {
        try { query(); return true; }
        catch (Exception ex)
        {
            Message = $"Could not read from the database: {ex.Message}. Press Reload to try again.";
            return false;
        }
    }

    [RelayCommand]
    private void Open(AlertRow? row) { if (row is not null) _open(row.PalletId); }

    [RelayCommand]
    private void Acknowledge(AlertRow? row)
    {
        if (row is null || row.IsAcknowledged) return;
        var comment = Views.InputDialog.Ask("Mark alert as handled",
            $"{row.TypeText} · {row.FolioCode}\n{row.Message}\n\nDescribe the action taken (required):",
            System.Windows.Application.Current.MainWindow);
        if (string.IsNullOrWhiteSpace(comment)) return;
        var now = Services.Clock.UtcNow;
        try
        {
            Services.AlertAcks.Acknowledge(new AlertAcknowledgement(row.PalletId, row.Alert.Type, User.NtUser, comment.Trim(), now,
                row.Alert.Severity, row.Alert.Message, row.Alert.Owner));
        }
        catch (Exception ex) when (ex is DataRuleException or Oracle.ManagedDataAccess.Client.OracleException)
        {
            System.Windows.MessageBox.Show(ex.Message, "Alert not saved", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        Services.Audit.Record(new[] { new AuditEntry(row.PalletId, row.FolioCode, "Alert", row.TypeText, $"Handled: {comment.Trim()}", User.NtUser, now, null) });
        Refresh();
    }
}

// =====================================================================
// Users & Access: who is Owner / Champion / Proxy / Viewer in which department and VS.
// Only the Owner edits; profile change requests are approved or rejected here.
// =====================================================================
public sealed record MatrixRow(string ValueStream, string Qmm, string Mfq, string Gs, string Lop, string Mfo, string Low, string Hse);

/// <summary>Function of a user in one Value Stream of the matrix ("—" = no access there).</summary>
public sealed partial class AccessCell : ObservableObject
{
    public const string None = "—";
    public static readonly IReadOnlyList<string> Options = new[] { None, "Viewer", "Member", "Proxy", "Champion" };
    public AccessCell(int valueStreamId, string code) { ValueStreamId = valueStreamId; _code = code; }
    public int ValueStreamId { get; }
    [ObservableProperty] private string _code;
    public UserFunction? Function => Code == None ? null : Enum.Parse<UserFunction>(Code);
    public static string CodeOf(UserFunction? f) => f is null ? None : f.Value.ToString();
}

/// <summary>
/// One row of the access matrix: a user in one department, with the function in each Value Stream (columns), or an
/// Owner row (no department, every VS). Saved as the same rows as always: one per user x department x VS.
/// </summary>
public sealed partial class AccessMatrixRow : ObservableObject
{
    public AccessMatrixRow(string ntUser, string displayName, string? departmentCode, bool isOwner, IEnumerable<AccessCell> cells, bool isNew = false)
    {
        _ntUser = ntUser; _displayName = displayName; _departmentCode = departmentCode; _isOwner = isOwner; IsNew = isNew;
        Cells = cells.ToList();
        foreach (var c in Cells) c.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AllVs));
    }
    public bool IsNew { get; }
    [ObservableProperty] private string _ntUser;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string? _departmentCode;
    [ObservableProperty] private bool _isOwner;
    public IReadOnlyList<AccessCell> Cells { get; }
    public bool HasDepartment => !IsOwner;

    partial void OnIsOwnerChanged(bool value) => OnPropertyChanged(nameof(HasDepartment));

    /// <summary>"All VS": shows the common function (empty when they differ) and sets every VS at once.</summary>
    public string? AllVs
    {
        get => Cells.Select(c => c.Code).Distinct().Count() == 1 ? Cells[0].Code : null;
        set { if (value is null) return; foreach (var c in Cells) c.Code = value; OnPropertyChanged(); }
    }

    public IEnumerable<UserAccess> ToAccess()
    {
        var nt = NtUsers.Normalize(NtUser);
        if (IsOwner) { yield return new UserAccess(nt, "", 0, UserFunction.Owner); yield break; }
        foreach (var c in Cells.Where(c => c.Function is not null))
            yield return new UserAccess(nt, DepartmentCode ?? "", c.ValueStreamId, c.Function!.Value);
    }
}

public sealed record RequestRow(ProfileRequest Request, string DisplayName, string CurrentDepartment, string CurrentValueStreams)
{
    public int Id => Request.Id;
    public string NtUser => Request.NtUser;
    public string TypeText => Request.TypeText;
    public string RequestedDepartment => Request.DepartmentCode;
    public string RequestedValueStreams => Request.ValueStreamText;
    public DateTime RequestedAtLocal => Request.RequestedAtUtc.ToLocalTime();
    public string StatusText => Request.Status.ToString();
    public string? DecidedBy => Request.DecidedBy;
    public DateTime? DecidedAtLocal => Request.DecidedAtUtc?.ToLocalTime();
    public string? Comment => Request.Comment;
    public bool IsPending => Request.Status == RequestStatus.Pending;
}

// =====================================================================
// Requests (Owner): new users and profile changes
// =====================================================================
public sealed partial class RequestsViewModel : PageViewModel
{
    private readonly UserContext _realUser;

    public RequestsViewModel(AppServices services, UserContext user, UserContext realUser) : base(services, user)
    {
        _realUser = realUser;
        Refresh();
    }

    public override string Title => "Requests";
    /// <summary>Only the real Owner decides (not someone previewing as another user).</summary>
    public bool CanEdit => _realUser.IsOwner && User.NtUser == _realUser.NtUser;

    [ObservableProperty] private IReadOnlyList<RequestRow> _pending = Array.Empty<RequestRow>();
    [ObservableProperty] private IReadOnlyList<RequestRow> _history = Array.Empty<RequestRow>();
    [ObservableProperty] private string? _message;
    public bool HasPending => Pending.Count > 0;
    public string PendingText => Pending.Count == 0 ? "No pending requests." : $"{Pending.Count} pending request(s)";
    partial void OnPendingChanged(IReadOnlyList<RequestRow> value) { OnPropertyChanged(nameof(HasPending)); OnPropertyChanged(nameof(PendingText)); }

    protected override void UserChanged() => OnPropertyChanged(nameof(CanEdit));

    public override void Refresh()
    {
        Services.Users.Refresh();
        var users = Services.Users.Users().ToDictionary(u => u.NtUser, StringComparer.OrdinalIgnoreCase);
        RequestRow Row(ProfileRequest r) => new(r, users.TryGetValue(r.NtUser, out var u) ? u.DisplayName : r.NtUser,
            users.TryGetValue(r.NtUser, out var u1) ? u1.DepartmentCode ?? "—" : "—",
            users.TryGetValue(r.NtUser, out var u2) ? u2.ValueStreamText : "—");
        var all = Services.Users.Requests(pendingOnly: false);
        Pending = all.Where(r => r.Status == RequestStatus.Pending).OrderBy(r => r.RequestedAtUtc).Select(Row).ToList();
        History = all.Where(r => r.Status != RequestStatus.Pending).OrderByDescending(r => r.DecidedAtUtc ?? r.RequestedAtUtc).Take(200).Select(Row).ToList();
    }

    [RelayCommand]
    private void Reload() { Refresh(); Message = null; }

    [RelayCommand]
    private void Approve(RequestRow? row) => Decide(row, true);

    [RelayCommand]
    private void Reject(RequestRow? row) => Decide(row, false);

    private void Decide(RequestRow? row, bool approve)
    {
        if (row is null || !CanEdit || !row.IsPending) return;
        var what = row.Request.IsRegistration
            ? $"New user · {row.RequestedDepartment} · {row.RequestedValueStreams}" + (approve ? "\nApproving makes them Member (capture) in those VS." : "\nRejecting keeps them as Viewer.")
            : $"{row.CurrentDepartment} · {row.CurrentValueStreams}  →  {row.RequestedDepartment} · {row.RequestedValueStreams}";
        var comment = Views.InputDialog.Ask(approve ? "Approve request" : "Reject request",
            $"{row.DisplayName} ({row.NtUser})\n{what}\n\nComment (optional):",
            System.Windows.Application.Current.MainWindow, allowEmpty: true);
        if (comment is null) return;
        try { Services.Users.Decide(row.Id, approve, _realUser.NtUser, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()); }
        catch (Exception ex) { Message = $"Not saved: {ex.Message}"; return; }
        Services.Audit.Record(new[] { new AuditEntry(0, "USERS", row.Request.IsRegistration ? "Registration request" : "Profile request",
            $"{row.CurrentDepartment} · {row.CurrentValueStreams}",
            $"{(approve ? "Approved" : "Rejected")}: {row.RequestedDepartment} · {row.RequestedValueStreams} ({row.NtUser})", _realUser.NtUser, Services.Clock.UtcNow, comment) });
        Message = !approve ? "Request rejected."
            : row.Request.IsRegistration
                ? $"Approved. {row.DisplayName} is now Member in {row.RequestedDepartment} · {row.RequestedValueStreams}. Promote to Proxy / Champion in Users & Access."
                : $"Approved. {row.DisplayName} now has {row.RequestedDepartment} · {row.RequestedValueStreams}.";
        Refresh();
    }
}

public sealed partial class UsersViewModel : PageViewModel
{
    private static readonly RoleCode[] MatrixAreas =
        { RoleCode.Qmm, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse };
    private readonly UserContext _realUser;

    public UsersViewModel(AppServices services, UserContext user, UserContext realUser) : base(services, user)
    {
        _realUser = realUser;
        Refresh();
    }

    public override string Title => "Users & Access";
    /// <summary>Only the real Owner edits (not someone previewing as another user).</summary>
    public bool CanEdit => _realUser.IsOwner && User.NtUser == _realUser.NtUser;
    public IReadOnlyList<LookupItem> DepartmentOptions => Catalogs.Departments.Select(d => new LookupItem(d.Code, d.Label)).ToList();

    /// <summary>Owner: add departments, or stop / start offering them (IB_PROCESS_DEPARTMENTS).</summary>
    [RelayCommand]
    private void OpenDepartments()
    {
        if (!CanEdit) return;
        new Views.DepartmentsWindow(Services, _realUser) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        OnPropertyChanged(nameof(DepartmentOptions));
        Refresh();
    }

    public string OwnersConfigText =>
        $"Built-in Owner: {string.Join(", ", Services.Config.Owners)}. Other Owners are the OWNER rows of this table (IB_PROCESS_USER_ACCESS).";

    [ObservableProperty] private IReadOnlyList<MatrixRow> _rows = Array.Empty<MatrixRow>();
    [ObservableProperty] private AccessMatrixRow? _selectedEdit;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private LookupItem? _departmentFilter;
    /// <summary>The access matrix: one row per user and department, one column per Value Stream (built by the view).</summary>
    public ObservableCollection<AccessMatrixRow> EditRows { get; } = new();
    public ICollectionView EditView { get; private set; } = null!;
    public IReadOnlyList<int> MatrixValueStreams { get; } = Catalogs.ValueStreams.Select(v => v.Id).ToList();
    public IReadOnlyList<string> CellOptions => AccessCell.Options;
    public IReadOnlyList<LookupItem> DepartmentFilterOptions =>
        new[] { new LookupItem(null, "All departments"), new LookupItem("OWNER", "Owners") }.Concat(DepartmentOptions).ToList();
    [ObservableProperty] private int _shownUsers;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnDepartmentFilterChanged(LookupItem? value) => ApplyFilter();

    private bool Keep(object o)
    {
        if (o is not AccessMatrixRow r) return false;
        if (r.IsNew) return true;                                  // a row being added is always visible
        var q = SearchText.Trim();
        if (q.Length > 0 && !r.NtUser.Contains(q, StringComparison.OrdinalIgnoreCase) && !r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
        return DepartmentFilter?.Value switch
        {
            null => true,
            "OWNER" => r.IsOwner,
            string d => !r.IsOwner && r.DepartmentCode == d,
            _ => true,
        };
    }

    private void ApplyFilter()
    {
        if (EditView is null) return;
        EditView.Refresh();
        ShownUsers = EditView.Cast<object>().Count();
    }

    public IReadOnlyList<string> EditableByArea => MatrixAreas
        .Select(r => $"{r.DisplayName()}: {string.Join(", ", FieldPermissions.EditableFields(r).Select(FieldLabels.Get))}").ToList();

    protected override void UserChanged() => OnPropertyChanged(nameof(CanEdit));

    public override void Refresh()
    {
        var users = Services.Users.Users().ToDictionary(u => u.NtUser);
        var access = Services.Users.Access();
        string Name(string nt) => users.TryGetValue(nt, out var u) ? u.DisplayName : nt;
        string Cell(int vs, RoleCode area) => string.Join("\n", access
            .Where(a => a.ValueStreamId == vs && a.Function is UserFunction.Champion or UserFunction.Proxy && RoleInfo.AreaOfDepartment(a.DepartmentCode) == area)
            .OrderBy(a => a.Function == UserFunction.Champion ? 0 : 1).Select(a => $"{Name(a.NtUser)} ({a.Function})"));
        Rows = Catalogs.ValueStreams.Select(v => v.Id)
            .Select(vs => new MatrixRow($"VS{vs}", Cell(vs, RoleCode.Qmm), Cell(vs, RoleCode.Requestor), Cell(vs, RoleCode.Gs),
                                        Cell(vs, RoleCode.Lop), Cell(vs, RoleCode.Mfo), Cell(vs, RoleCode.Low), Cell(vs, RoleCode.Hse)))
            .ToList();

        EditRows.Clear();
        var owners = access.Where(a => a.Function == UserFunction.Owner).Select(a => a.NtUser).Distinct();
        foreach (var nt in owners.OrderBy(Name))
            EditRows.Add(new AccessMatrixRow(nt, Name(nt), null, true, MatrixValueStreams.Select(v => new AccessCell(v, AccessCell.None))));
        foreach (var g in access.Where(a => a.Function != UserFunction.Owner).GroupBy(a => (a.NtUser, a.DepartmentCode))
                     .OrderBy(g => Name(g.Key.NtUser)).ThenBy(g => g.Key.DepartmentCode))
            EditRows.Add(new AccessMatrixRow(g.Key.NtUser, Name(g.Key.NtUser), g.Key.DepartmentCode, false,
                MatrixValueStreams.Select(v => new AccessCell(v, AccessCell.CodeOf(g.FirstOrDefault(a => a.ValueStreamId == v)?.Function)))));
        EditView = CollectionViewSource.GetDefaultView(EditRows);
        EditView.Filter = Keep;
        OnPropertyChanged(nameof(EditView));
        DepartmentFilter ??= DepartmentFilterOptions[0];
        ApplyFilter();
    }

    [RelayCommand]
    private void AddAccess()
    {
        // New row: Windows user, department and the function in each VS. A selected user is copied (another department).
        var row = new AccessMatrixRow(SelectedEdit?.NtUser ?? "", SelectedEdit?.DisplayName ?? "", null, false,
            MatrixValueStreams.Select(v => new AccessCell(v, AccessCell.None)), isNew: true);
        // Typing the Windows user of someone already registered brings their name (it can still be changed for a new user).
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AccessMatrixRow.NtUser) || !string.IsNullOrWhiteSpace(row.DisplayName)) return;
            if (Services.Users.Find(row.NtUser) is { } known) row.DisplayName = known.DisplayName;
        };
        EditRows.Add(row);
        SelectedEdit = row;
        ApplyFilter();
        Message = row.NtUser.Length > 0
            ? $"New row for {row.DisplayName}: choose the department and the function in each VS, then save."
            : "Enter the Windows user (without domain, e.g. RLN8JU), the name, Owner or the department, and the function in each VS, then save.";
    }

    [RelayCommand]
    private void RemoveAccess()
    {
        if (SelectedEdit is null) { Message = "Select the row whose access ends."; return; }
        var row = SelectedEdit;
        row.IsOwner = false;
        row.AllVs = AccessCell.None;
        Message = $"Access of {row.DisplayName} ({row.DepartmentCode ?? "Owner"}) will end when you press Save changes (kept as history, never deleted).";
    }

    [RelayCommand]
    private void SaveChanges()
    {
        if (!CanEdit) return;
        if (EditRows.Any(r => string.IsNullOrWhiteSpace(r.NtUser) && (r.IsOwner || r.Cells.Any(c => c.Function is not null))))
        { Message = "Every row needs a Windows user."; return; }
        if (EditRows.Any(r => !r.IsOwner && string.IsNullOrEmpty(r.DepartmentCode) && r.Cells.Any(c => c.Function is not null)))
        { Message = "Choose the department of every row with access (only Owner rows have none)."; return; }
        // A user that is not registered yet is created with the name typed in its new row.
        var newRows = EditRows.Where(r => r.IsNew && !string.IsNullOrWhiteSpace(r.NtUser) && (r.IsOwner || r.Cells.Any(c => c.Function is not null))).ToList();
        var unnamed = newRows.FirstOrDefault(r => Services.Users.Find(r.NtUser) is null && string.IsNullOrWhiteSpace(r.DisplayName));
        if (unnamed is not null) { Message = $"Enter the name of {NtUsers.Normalize(unnamed.NtUser)} (new user)."; return; }
        var newUserNames = newRows.Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .GroupBy(r => NtUsers.Normalize(r.NtUser)).ToDictionary(g => g.Key, g => g.Last().DisplayName.Trim());
        var after = EditRows.SelectMany(r => r.ToAccess()).ToList();
        var dup = after.GroupBy(a => (a.NtUser, a.DepartmentCode, a.ValueStreamId)).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            var where = dup.Key.ValueStreamId == 0 ? "Owner" : $"{dup.Key.DepartmentCode} · VS{dup.Key.ValueStreamId}";
            Message = $"{dup.Key.NtUser} appears twice for {where}. Keep one row per user and department.";
            return;
        }
        if (!after.Any(a => a.Function == UserFunction.Owner) && Services.Config.Owners.Count == 0)
        { Message = "At least one Owner is required."; return; }

        var before = Services.Users.Access().ToList();
        var removed = before.Where(b => !after.Contains(b)).ToList();
        var added = after.Where(a => !before.Contains(a)).ToList();
        if (removed.Count + added.Count == 0) { Message = "No changes to save."; return; }

        var now = Services.Clock.UtcNow;
        try
        {
            // Nothing is deleted: access that disappears is closed (VALID_TO) and stays as history.
            Services.Users.ReplaceAccess(after, _realUser.NtUser, newUserNames);      // raises Changed: the shell recalculates the current user's access
        }
        catch (Exception ex)
        {
            Message = $"Not saved: {ex.Message}";
            return;
        }
        Services.Audit.Record(removed.Select(p => new AuditEntry(0, "USERS", "Users & Access", p.Describe, "(access ended, kept as history)", _realUser.NtUser, now, "Ended"))
            .Concat(added.Select(p => new AuditEntry(0, "USERS", "Users & Access", null, p.Describe, _realUser.NtUser, now, "Added"))));
        Refresh();
        Message = $"Access updated: {added.Count} added, {removed.Count} ended (kept as history). It applies immediately.";
    }

    [RelayCommand]
    private void DiscardChanges() { Refresh(); Message = null; }
}

// =====================================================================
// Audit log
// =====================================================================
public sealed partial class AuditViewModel : PageViewModel
{
    /// <summary>The log only grows: it is read page by page from the database (filters and order in SQL), like the inbox.</summary>
    public const int PageSize = 50;
    private readonly DispatcherTimer _folioDelay;
    private bool _loadingFilters;
    private AuditFilter _filter = AuditFilter.All;

    public AuditViewModel(AppServices services, UserContext user) : base(services, user)
    {
        // Typing a folio asks the database once the user pauses, not on every key.
        _folioDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _folioDelay.Tick += (_, _) => { _folioDelay.Stop(); Apply(); };
        Refresh();
    }

    public override string Title => "Audit log";
    public ObservableCollection<AuditEntry> Entries { get; } = new();
    public ObservableCollection<PageButton> PageButtons { get; } = new();
    [ObservableProperty] private IReadOnlyList<string> _userOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<LookupItem> _fieldOptions = Array.Empty<LookupItem>();
    [ObservableProperty] private string _folioFilter = "";
    [ObservableProperty] private string _selectedUser = "All";
    [ObservableProperty] private LookupItem? _selectedField;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _pageSummary = "";
    [ObservableProperty] private string? _message;
    public bool HasSeveralPages => PageCount > 1;
    public bool HasRecords => VisibleCount > 0;

    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(HasSeveralPages));
    partial void OnVisibleCountChanged(int value) => OnPropertyChanged(nameof(HasRecords));
    partial void OnFolioFilterChanged(string value)
    {
        if (_loadingFilters) return;
        _folioDelay.Stop();
        _folioDelay.Start();
    }
    partial void OnSelectedUserChanged(string value) => Apply();
    partial void OnSelectedFieldChanged(LookupItem? value) => Apply();
    partial void OnFromChanged(DateTime? value) => Apply();
    partial void OnToChanged(DateTime? value) => Apply();

    [RelayCommand] private void FirstPage() => GoToPage(1);
    [RelayCommand] private void PreviousPage() => GoToPage(Page - 1);
    [RelayCommand] private void NextPage() => GoToPage(Page + 1);
    [RelayCommand] private void LastPage() => GoToPage(PageCount);
    [RelayCommand] private void GoToPage(int? page)
    {
        if (page is not { } target) return;                        // the "…" of the pager
        Page = Math.Clamp(target, 1, Math.Max(1, PageCount));
        ShowPage();
    }

    /// <summary>Reload: options of the filters (users and fields in the log) and the current page again.</summary>
    public override void Refresh()
    {
        Message = null;
        if (!Try(() =>
            {
                var fieldKey = SelectedField?.Value as string;
                _loadingFilters = true;
                try
                {
                    UserOptions = new[] { "All" }.Concat(Services.Audit.Users()).ToList();
                    FieldOptions = new[] { new LookupItem(null, "All fields") }
                        .Concat(Services.Audit.Fields().OrderBy(FieldLabels.Get).Select(f => new LookupItem(f, FieldLabels.Get(f)))).ToList();
                    SelectedField = FieldOptions.FirstOrDefault(o => Equals(o.Value, fieldKey)) ?? FieldOptions[0];
                    if (!UserOptions.Contains(SelectedUser)) SelectedUser = "All";
                }
                finally { _loadingFilters = false; }
            })) return;
        Apply(keepPage: true);
    }

    /// <summary>The count follows the filters (one COUNT query) and the page is read again from page 1.</summary>
    private void Apply(bool keepPage = false)
    {
        if (_loadingFilters) return;
        _filter = new AuditFilter(FolioFilter, SelectedUser is "All" or null ? null : SelectedUser, SelectedField?.Value as string, From, To);
        if (!keepPage) Page = 1;
        if (!Try(() => VisibleCount = Services.Audit.Count(_filter))) return;
        ShowPage();
    }

    private void ShowPage()
    {
        PageCount = Math.Max(1, (int)Math.Ceiling(VisibleCount / (double)PageSize));
        if (Page > PageCount) Page = PageCount;
        Entries.Clear();
        PageButtons.Clear();
        var first = (Page - 1) * PageSize;
        if (VisibleCount > 0 && !Try(() => { foreach (var e in Services.Audit.Page(_filter, first, PageSize)) Entries.Add(e); })) return;
        PageSummary = VisibleCount == 0 ? "No changes" : $"Changes {first + 1:N0}–{first + Entries.Count:N0} of {VisibleCount:N0}";
        foreach (var b in PageButton.Build(Page, PageCount)) PageButtons.Add(b);
    }

    /// <summary>A query that fails (network, database) shows the reason instead of closing the screen.</summary>
    private bool Try(Action query)
    {
        try { query(); return true; }
        catch (Exception ex)
        {
            Message = $"Could not read from the database: {ex.Message}. Press Reload to try again.";
            return false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _loadingFilters = true;
        try
        {
            FolioFilter = "";
            SelectedUser = "All";
            SelectedField = FieldOptions.FirstOrDefault();
            From = null;
            To = null;
        }
        finally { _loadingFilters = false; }
        Apply();
    }
}
