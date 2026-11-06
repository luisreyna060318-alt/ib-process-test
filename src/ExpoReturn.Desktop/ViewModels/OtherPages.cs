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
// PNT-03 · Balance / Aranceles  (UX-B13: clic en un VS o estatus -> bandeja filtrada)
// =====================================================================
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<ListFilter> _openList;

    public DashboardViewModel(AppServices services, UserContext user, Action<ListFilter> openList) : base(services, user)
    {
        _openList = openList;
        YearOptions = new[] { new LookupItem(null, "Todos los años") }
            .Concat(services.Reporting.FiscalYears().Select(y => new LookupItem(y, $"Año fiscal {y} (por fecha de cruce)"))).ToList();
        _selectedYear = YearOptions[0];
        Refresh();
    }

    public override string Title => "Balance / Aranceles";
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
// PNT-04 · Alertas  (UX-B09: agrupar por área responsable; marcar como atendida)
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
    public string AckText => Ack is null ? "" : $"Atendida por {Ack.NtUser} el {Ack.AtUtc.ToLocalTime():dd/MM HH:mm}: {Ack.Comment}";
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

    public override string Title => "Alertas";
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
        var comment = Views.InputDialog.Ask("Marcar alerta como atendida",
            $"{row.TypeText} · {row.FolioCode}\n{row.Message}\n\nDescriba la acción tomada (obligatorio):",
            System.Windows.Application.Current.MainWindow);
        if (string.IsNullOrWhiteSpace(comment)) return;
        var now = Services.Clock.UtcNow;
        Services.AlertAcks.Acknowledge(new AlertAcknowledgement(row.PalletId, row.Alert.Type, User.NtUser, comment.Trim(), now));
        Services.Audit.Record(new[] { new AuditEntry(row.PalletId, row.FolioCode, "Alerta", row.TypeText, $"Atendida: {comment.Trim()}", User.NtUser, now, null) });
        Refresh();
    }
}

// =====================================================================
// PNT-05 · Champions VS  (UX-B10: edición por Admin QMM/PE con vigencia)
// =====================================================================
public sealed record ChampionRow(string ValueStream, string Mfo, string Qmm, string Low, string Gs, string Mfq);

public sealed partial class ChampionEditRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private RoleCode _role = RoleCode.Qmm;
    [ObservableProperty] private int _valueStreamId = 1;
    [ObservableProperty] private string _assignment = "Champion";
    [ObservableProperty] private DateTime? _validFrom;
    [ObservableProperty] private DateTime? _validTo;

    public static ChampionEditRow From(Person p) => new()
    {
        Name = p.Name, Role = p.Role, ValueStreamId = p.ValueStreamId, Assignment = p.Assignment, ValidFrom = p.ValidFrom, ValidTo = p.ValidTo,
    };
    public Person ToPerson() => new(Name.Trim(), Role, ValueStreamId, Assignment, ValidFrom, ValidTo);
}

public sealed partial class ChampionsViewModel : PageViewModel
{
    public ChampionsViewModel(AppServices services, UserContext user) : base(services, user) => Refresh();

    public override string Title => "Champions VS";
    public bool CanEdit => User.IsAdmin;
    public IReadOnlyList<LookupItem> RoleOptions { get; } = new[] { RoleCode.Mfo, RoleCode.Qmm, RoleCode.Low, RoleCode.Gs, RoleCode.Requestor, RoleCode.Lop, RoleCode.Hse }
        .Select(r => new LookupItem(r, r switch { RoleCode.Mfo => "MFO", RoleCode.Qmm => "QMM", RoleCode.Low => "LOW", RoleCode.Gs => "GS / OSD43",
                                                     RoleCode.Requestor => "MFQ", RoleCode.Lop => "LOP", _ => "HSE" })).ToList();
    public IReadOnlyList<int> ValueStreamOptions { get; } = Catalogs.ValueStreams.Select(v => v.Id).ToList();
    public IReadOnlyList<string> AssignmentOptions { get; } = new[] { "Champion", "Proxy" };

    [ObservableProperty] private IReadOnlyList<ChampionRow> _rows = Array.Empty<ChampionRow>();
    [ObservableProperty] private ChampionEditRow? _selectedEdit;
    [ObservableProperty] private string? _message;
    public ObservableCollection<ChampionEditRow> EditRows { get; } = new();

    public IReadOnlyList<string> EditableByRole => Enum.GetValues<RoleCode>()
        .Select(r => FieldPermissions.EditableFields(r).Count == 0
            ? $"{r.DisplayName()}: (sólo lectura, no edita campos)"
            : $"{r.DisplayName()}: {string.Join(", ", FieldPermissions.EditableFields(r).Select(FieldLabels.Get))}").ToList();

    protected override void UserChanged() => OnPropertyChanged(nameof(CanEdit));

    public override void Refresh()
    {
        var today = Services.Clock.Today;
        var people = Services.Champions.All().Where(p => p.IsActiveOn(today)).ToList();
        string Cell(int vs, RoleCode role) => string.Join("  /  ", people.Where(p => p.ValueStreamId == vs && p.Role == role)
            .OrderBy(p => p.Assignment == "Champion" ? 0 : 1).Select(p => $"{p.Name} ({p.Assignment})"));
        Rows = people.Select(p => p.ValueStreamId).Distinct().OrderBy(v => v)
            .Select(vs => new ChampionRow($"VS{vs}", Cell(vs, RoleCode.Mfo), Cell(vs, RoleCode.Qmm), Cell(vs, RoleCode.Low), Cell(vs, RoleCode.Gs), Cell(vs, RoleCode.Requestor)))
            .ToList();
        EditRows.Clear();
        foreach (var p in Services.Champions.All().OrderBy(p => p.ValueStreamId).ThenBy(p => p.Role).ThenBy(p => p.Assignment))
            EditRows.Add(ChampionEditRow.From(p));
    }

    [RelayCommand]
    private void AddPerson()
    {
        var row = new ChampionEditRow { ValidFrom = Services.Clock.Today };
        EditRows.Add(row);
        SelectedEdit = row;
    }

    /// <summary>Dar de baja = cerrar la vigencia (no se borra: se conserva la historia).</summary>
    [RelayCommand]
    private void EndPerson()
    {
        if (SelectedEdit is null) return;
        SelectedEdit.ValidTo = Services.Clock.Today.AddDays(-1);
    }

    [RelayCommand]
    private void SaveChanges()
    {
        if (!CanEdit) return;
        var invalid = EditRows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Name) || (r.ValidTo is not null && r.ValidFrom is not null && r.ValidTo < r.ValidFrom));
        if (invalid is not null) { Message = "Revise los renglones: el nombre es obligatorio y 'Vigente hasta' no puede ser anterior a 'Vigente desde'."; return; }
        var before = Services.Champions.All().Select(p => p.ToString()).ToHashSet();
        var after = EditRows.Select(r => r.ToPerson()).ToList();
        var changes = after.Count(p => !before.Contains(p.ToString())) + before.Count(b => after.All(p => p.ToString() != b));
        Services.Champions.Replace(after);
        Services.Audit.Record(new[] { new AuditEntry(0, "CHAMPIONS", "Matriz Champions VS", null, $"{changes} cambio(s)", User.NtUser, Services.Clock.UtcNow, null) });
        Message = $"Matriz actualizada ({changes} cambio(s)). Aplica de inmediato a permisos y listas de Responsable QMM.";
        Refresh();
    }

    [RelayCommand]
    private void DiscardChanges() { Message = null; Refresh(); }
}

// =====================================================================
// PNT-06 · Bitácora  (UX-B08: filtros por folio, usuario, campo y fechas)
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

    public override string Title => "Bitácora";
    public ICollectionView Entries { get; }
    [ObservableProperty] private IReadOnlyList<string> _userOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<LookupItem> _fieldOptions = Array.Empty<LookupItem>();
    [ObservableProperty] private string _folioFilter = "";
    [ObservableProperty] private string _selectedUser = "Todos";
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
        UserOptions = new[] { "Todos" }.Concat(_entries.Select(e => e.NtUser).Distinct().OrderBy(u => u)).ToList();
        FieldOptions = new[] { new LookupItem(null, "Todos los campos") }
            .Concat(_entries.Select(e => e.Field).Distinct().OrderBy(f => FieldLabels.Get(f)).Select(f => new LookupItem(f, FieldLabels.Get(f)))).ToList();
        SelectedField ??= FieldOptions[0];
        Apply();
    }

    private bool Filter(object o)
    {
        if (o is not AuditEntry e) return false;
        if (!string.IsNullOrWhiteSpace(FolioFilter) && !e.FolioCode.Contains(FolioFilter.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedUser is { } u && u != "Todos" && e.NtUser != u) return false;
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
        SelectedUser = "Todos";
        SelectedField = FieldOptions.FirstOrDefault();
        From = null;
        To = null;
    }
}
