using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop.ViewModels;

public sealed record BarItem(string Label, decimal Value, double Width, string ValueText, int? ValueStreamId = null, WorkflowStatus? Status = null);

// =====================================================================
// PNT-03 · Balance / Duties  (UX-B13: click a VS or status -> filtered inbox)
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
// PNT-04 · Alerts  (UX-B09: grouped by responsible area; mark as handled)
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
    private readonly Action<int> _open;
    private readonly ObservableCollection<AlertRow> _rows = new();

    public AlertsViewModel(AppServices services, UserContext user, Action<int> open) : base(services, user)
    {
        _open = open;
        Alerts = CollectionViewSource.GetDefaultView(_rows);
        Alerts.Filter = o => o is AlertRow r && (ShowAcknowledged || !r.IsAcknowledged);
        Alerts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AlertRow.Owner)));
        Refresh();
    }

    public override string Title => "Alerts";
    public ICollectionView Alerts { get; }
    [ObservableProperty] private int _critical;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _acknowledged;
    [ObservableProperty] private bool _showAcknowledged;

    partial void OnShowAcknowledgedChanged(bool value) => Alerts.Refresh();

    public override void Refresh()
    {
        _rows.Clear();
        foreach (var a in Services.Reporting.Alerts())
            _rows.Add(new AlertRow(a, Services.AlertAcks.Find(a.PalletId, a.Type)));
        var pending = _rows.Where(r => !r.IsAcknowledged).ToList();
        Critical = pending.Count(a => a.Severity == Severity.Error);
        Warnings = pending.Count(a => a.Severity == Severity.Warning);
        Acknowledged = _rows.Count(r => r.IsAcknowledged);
        Alerts.Refresh();
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
// PNT-05 · Users & Access: who is Owner / Champion / Proxy / Viewer in which department and VS.
// Only the Owner edits; profile change requests are approved or rejected here.
// =====================================================================
public sealed record MatrixRow(string ValueStream, string Qmm, string Mfq, string Gs, string Lop, string Mfo, string Low, string Hse);

public sealed partial class AccessEditRow : ObservableObject
{
    [ObservableProperty] private string _ntUser = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string? _departmentCode;
    [ObservableProperty] private int _valueStreamId = 1;
    [ObservableProperty] private UserFunction _function = UserFunction.Viewer;

    public static AccessEditRow From(UserAccess a, string? name) => new()
    {
        NtUser = a.NtUser, DisplayName = name ?? a.NtUser, DepartmentCode = a.Function == UserFunction.Owner ? null : a.DepartmentCode,
        ValueStreamId = a.Function == UserFunction.Owner ? 0 : a.ValueStreamId, Function = a.Function,
    };
    public UserAccess ToAccess() => Function == UserFunction.Owner
        ? new UserAccess(NtUser.Trim(), "", 0, UserFunction.Owner)
        : new UserAccess(NtUser.Trim(), DepartmentCode ?? "", ValueStreamId, Function);
}

public sealed record RequestRow(ProfileRequest Request, string DisplayName, string CurrentDepartment, string CurrentValueStreams)
{
    public int Id => Request.Id;
    public string NtUser => Request.NtUser;
    public string RequestedDepartment => Request.DepartmentCode;
    public string RequestedValueStreams => Request.ValueStreamText;
    public DateTime RequestedAtLocal => Request.RequestedAtUtc.ToLocalTime();
}

public sealed partial class UsersViewModel : PageViewModel
{
    private static readonly RoleCode[] MatrixAreas =
        { RoleCode.Qmm, RoleCode.Requestor, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse };
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

    public IReadOnlyList<int> ValueStreamOptions { get; } = new[] { 0 }.Concat(Catalogs.ValueStreams.Select(v => v.Id)).ToList();
    public IReadOnlyList<UserFunction> FunctionOptions { get; } = Enum.GetValues<UserFunction>();
    public string OwnersConfigText =>
        $"Built-in Owner: {string.Join(", ", Services.Config.Owners)}. Other Owners are the OWNER rows of this table (IB_PROCESS_USER_ACCESS).";

    [ObservableProperty] private IReadOnlyList<MatrixRow> _rows = Array.Empty<MatrixRow>();
    [ObservableProperty] private IReadOnlyList<RequestRow> _requests = Array.Empty<RequestRow>();
    [ObservableProperty] private AccessEditRow? _selectedEdit;
    [ObservableProperty] private string? _message;
    public bool HasRequests => Requests.Count > 0;
    public ObservableCollection<AccessEditRow> EditRows { get; } = new();

    partial void OnRequestsChanged(IReadOnlyList<RequestRow> value) => OnPropertyChanged(nameof(HasRequests));

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
        Requests = Services.Users.Requests().Select(r => new RequestRow(r, Name(r.NtUser),
            users.TryGetValue(r.NtUser, out var u) ? u.DepartmentCode ?? "—" : "—",
            users.TryGetValue(r.NtUser, out var u2) ? u2.ValueStreamText : "—")).ToList();
        EditRows.Clear();
        foreach (var a in access.OrderBy(a => a.Function == UserFunction.Owner ? 0 : 1).ThenBy(a => Name(a.NtUser)).ThenBy(a => a.ValueStreamId))
            EditRows.Add(AccessEditRow.From(a, Name(a.NtUser)));
    }

    [RelayCommand]
    private void Approve(RequestRow? row) => Decide(row, true);

    [RelayCommand]
    private void Reject(RequestRow? row) => Decide(row, false);

    private void Decide(RequestRow? row, bool approve)
    {
        if (row is null || !CanEdit) return;
        var comment = Views.InputDialog.Ask(approve ? "Approve request" : "Reject request",
            $"{row.DisplayName} ({row.NtUser})\n{row.CurrentDepartment} · {row.CurrentValueStreams}  →  {row.RequestedDepartment} · {row.RequestedValueStreams}\n\nComment (optional):",
            System.Windows.Application.Current.MainWindow, allowEmpty: true);
        if (comment is null) return;
        try { Services.Users.Decide(row.Id, approve, _realUser.NtUser, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()); }
        catch (Exception ex) { Message = $"Not saved: {ex.Message}"; return; }
        Services.Audit.Record(new[] { new AuditEntry(0, "USERS", "Profile request", $"{row.CurrentDepartment} · {row.CurrentValueStreams}",
            $"{(approve ? "Approved" : "Rejected")}: {row.RequestedDepartment} · {row.RequestedValueStreams} ({row.NtUser})", _realUser.NtUser, Services.Clock.UtcNow, comment) });
        Message = approve
            ? $"Request approved. {row.DisplayName} now has {row.RequestedDepartment} · {row.RequestedValueStreams}; new VS start as Viewer."
            : "Request rejected.";
        Refresh();
    }

    [RelayCommand]
    private void AddAccess()
    {
        // Copies the selected user so another VS or function can be assigned with one click.
        var row = SelectedEdit is null ? new AccessEditRow()
            : new AccessEditRow { NtUser = SelectedEdit.NtUser, DisplayName = SelectedEdit.DisplayName, DepartmentCode = SelectedEdit.DepartmentCode,
                                  ValueStreamId = SelectedEdit.ValueStreamId, Function = SelectedEdit.Function };
        EditRows.Add(row);
        SelectedEdit = row;
        Message = row.NtUser.Length > 0 ? $"New row for {row.DisplayName}: choose the VS / function and save." : "Enter the Windows user (DOMAIN\\user), department, VS and function, then save.";
    }

    [RelayCommand]
    private void RemoveAccess()
    {
        if (SelectedEdit is null) { Message = "Select the access row to end."; return; }
        var row = SelectedEdit;
        EditRows.Remove(row);
        SelectedEdit = null;
        Message = $"Access to end: {row.ToAccess().Describe}. Press Save changes to apply it (the row is kept as history, never deleted).";
    }

    [RelayCommand]
    private void SaveChanges()
    {
        if (!CanEdit) return;
        var after = EditRows.Select(r => r.ToAccess()).ToList();
        if (after.Any(a => string.IsNullOrWhiteSpace(a.NtUser))) { Message = "Every row needs a Windows user."; return; }
        if (after.Any(a => a.Function != UserFunction.Owner && (a.DepartmentCode.Length == 0 || a.ValueStreamId == 0)))
        { Message = "Viewer, Proxy and Champion rows need a department and a Value Stream (only Owner ignores them)."; return; }
        var dup = after.GroupBy(a => (a.NtUser.ToUpperInvariant(), a.DepartmentCode, a.ValueStreamId)).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) { Message = $"{dup.Key.Item1} appears twice for {dup.Key.DepartmentCode} · VS{dup.Key.ValueStreamId}. Keep one row."; return; }
        if (!after.Any(a => a.Function == UserFunction.Owner) && Services.Config.Owners.Count == 0)
        { Message = "At least one Owner is required."; return; }

        var before = Services.Users.Access().ToList();
        var norm = after.Select(a => a with { NtUser = a.NtUser.Trim().ToUpperInvariant() }).ToList();
        var removed = before.Where(b => !norm.Contains(b)).ToList();
        var added = norm.Where(a => !before.Contains(a)).ToList();
        if (removed.Count + added.Count == 0) { Message = "No changes to save."; return; }

        var now = Services.Clock.UtcNow;
        try
        {
            // Nothing is deleted: rows that disappear from the grid are closed (VALID_TO) and stay as history.
            Services.Users.ReplaceAccess(norm, _realUser.NtUser);      // raises Changed: the shell recalculates the current user's access
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
// PNT-06 · Audit log  (UX-B08: filters by folio, user, field and dates)
// =====================================================================
public sealed partial class AuditViewModel : PageViewModel
{
    private readonly ObservableCollection<AuditEntry> _entries = new();

    public AuditViewModel(AppServices services, UserContext user) : base(services, user)
    {
        Entries = CollectionViewSource.GetDefaultView(_entries);
        Entries.Filter = Filter;
        Refresh();
    }

    public override string Title => "Audit log";
    public ICollectionView Entries { get; }
    [ObservableProperty] private IReadOnlyList<string> _userOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<LookupItem> _fieldOptions = Array.Empty<LookupItem>();
    [ObservableProperty] private string _folioFilter = "";
    [ObservableProperty] private string _selectedUser = "All";
    [ObservableProperty] private LookupItem? _selectedField;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private int _visibleCount;

    partial void OnFolioFilterChanged(string value) => Apply();
    partial void OnSelectedUserChanged(string value) => Apply();
    partial void OnSelectedFieldChanged(LookupItem? value) => Apply();
    partial void OnFromChanged(DateTime? value) => Apply();
    partial void OnToChanged(DateTime? value) => Apply();

    public override void Refresh()
    {
        _entries.Clear();
        foreach (var e in Services.Audit.All()) _entries.Add(e);
        UserOptions = new[] { "All" }.Concat(_entries.Select(e => e.NtUser).Distinct().OrderBy(u => u)).ToList();
        FieldOptions = new[] { new LookupItem(null, "All fields") }
            .Concat(_entries.Select(e => e.Field).Distinct().OrderBy(f => FieldLabels.Get(f)).Select(f => new LookupItem(f, FieldLabels.Get(f)))).ToList();
        SelectedField ??= FieldOptions[0];
        Apply();
    }

    private bool Filter(object o)
    {
        if (o is not AuditEntry e) return false;
        if (!string.IsNullOrWhiteSpace(FolioFilter) && !e.FolioCode.Contains(FolioFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedUser is { } u && u != "All" && e.NtUser != u) return false;
        if (SelectedField?.Value is string f && e.Field != f) return false;
        var day = e.ChangedAtLocal.Date;
        if (From is { } from && day < from.Date) return false;
        if (To is { } to && day > to.Date) return false;
        return true;
    }

    private void Apply()
    {
        Entries.Refresh();
        VisibleCount = Entries.Cast<object>().Count();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FolioFilter = "";
        SelectedUser = "All";
        SelectedField = FieldOptions.FirstOrDefault();
        From = null;
        To = null;
    }
}
