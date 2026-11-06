using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;

namespace ExpoReturn.Desktop.ViewModels;

public sealed record BarItem(string Label, decimal Value, double Width, string ValueText);

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<int> _open;

    public DashboardViewModel(AppServices services, UserContext user, Action<int> open) : base(services, user)
    {
        _open = open;
        YearOptions = new[] { new LookupItem(null, "Todos los años") }
            .Concat(services.Reporting.FiscalYears().Select(y => new LookupItem(y, $"FY{y} (por fecha de cruce)"))).ToList();
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

    partial void OnSelectedYearChanged(LookupItem value) => Refresh();

    public override void Refresh()
    {
        var rows = Services.Reporting.BalanceByValueStream(SelectedYear?.Value as int?);
        Rows = rows;
        Total = rows.LastOrDefault(r => r.ValueStream == "Total");
        var vsRows = rows.Where(r => r.ValueStream != "Total").ToList();
        var max = vsRows.Select(r => r.BalanceGeneralUsd).DefaultIfEmpty(0).Max();
        BalanceBars = vsRows.Select(r => new BarItem(r.ValueStream, r.BalanceGeneralUsd,
            max == 0 ? 0 : (double)(r.BalanceGeneralUsd / max) * 420, $"USD {r.BalanceGeneralUsd:N0}")).ToList();
        var dist = Services.Reporting.StatusDistribution();
        var maxCount = dist.Select(d => d.Count).DefaultIfEmpty(0).Max();
        StatusBars = dist.Select(d => new BarItem(d.Name, d.Count, maxCount == 0 ? 0 : 300.0 * d.Count / maxCount, d.Count.ToString())).ToList();
    }
}

public sealed partial class AlertsViewModel : PageViewModel
{
    private readonly Action<int> _open;
    public AlertsViewModel(AppServices services, UserContext user, Action<int> open) : base(services, user)
    {
        _open = open;
        Refresh();
    }

    public override string Title => "Alertas";
    [ObservableProperty] private IReadOnlyList<Alert> _alerts = Array.Empty<Alert>();
    [ObservableProperty] private int _critical;
    [ObservableProperty] private int _warnings;

    public override void Refresh()
    {
        Alerts = Services.Reporting.Alerts();
        Critical = Alerts.Count(a => a.Severity == Domain.Severity.Error);
        Warnings = Alerts.Count(a => a.Severity == Domain.Severity.Warning);
    }

    [RelayCommand]
    private void Open(Alert? alert) { if (alert is not null) _open(alert.PalletId); }
}

public sealed record ChampionRow(string ValueStream, string Mfo, string Qmm, string Low, string Gs, string Mfq);

public sealed partial class ChampionsViewModel : PageViewModel
{
    public ChampionsViewModel(AppServices services, UserContext user) : base(services, user) => Refresh();
    public override string Title => "Champions VS";
    [ObservableProperty] private IReadOnlyList<ChampionRow> _rows = Array.Empty<ChampionRow>();
    public IReadOnlyList<string> EditableByRole => Enum.GetValues<RoleCode>()
        .Select(r => FieldPermissions.EditableFields(r).Count == 0
            ? $"{r.DisplayName()}: (sólo lectura, no edita campos)"
            : $"{r.DisplayName()}: {string.Join(", ", FieldPermissions.EditableFields(r))}").ToList();

    public override void Refresh()
    {
        var people = Services.Champions.All();
        string Cell(int vs, RoleCode role) => string.Join("  /  ", people.Where(p => p.ValueStreamId == vs && p.Role == role)
            .OrderBy(p => p.Assignment == "Champion" ? 0 : 1).Select(p => $"{p.Name} ({p.Assignment})"));
        Rows = people.Select(p => p.ValueStreamId).Distinct().OrderBy(v => v)
            .Select(vs => new ChampionRow($"VS{vs}", Cell(vs, RoleCode.Mfo), Cell(vs, RoleCode.Qmm), Cell(vs, RoleCode.Low), Cell(vs, RoleCode.Gs), Cell(vs, RoleCode.Requestor)))
            .ToList();
    }
}

public sealed partial class AuditViewModel : PageViewModel
{
    public AuditViewModel(AppServices services, UserContext user) : base(services, user) => Refresh();
    public override string Title => "Bitácora";
    [ObservableProperty] private IReadOnlyList<AuditEntry> _entries = Array.Empty<AuditEntry>();
    public override void Refresh() => Entries = Services.Audit.All();
}
