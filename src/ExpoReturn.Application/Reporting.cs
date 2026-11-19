using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public sealed record BalanceRow(
    string ValueStream, int Pallets, int OpenPallets, int DutyRiskPallets,
    decimal TotalImportedUsd, decimal MfoInventoryUsd, decimal HseScrapUsd, decimal MfoLowFailureUsd,
    decimal ExportedUsd, decimal BalanceGeneralUsd, decimal DutiesToPayUsd);

public sealed record StatusCount(WorkflowStatus Status, int Count)
{
    public string Name => Status.DisplayName();
}

public enum AlertType { AgingWarning, AgingCritical, LegalExpired, ReworkSla, ExportSla, HseSla, QtyMismatch, CostPending }

public sealed record Alert(AlertType Type, Severity Severity, int PalletId, string FolioCode, string ValueStream, string Message, string Owner)
{
    public string TypeText => Type switch
    {
        AlertType.AgingWarning => "IMMEX aging (warning)",
        AlertType.AgingCritical => "IMMEX aging (critical)",
        AlertType.LegalExpired => "IMMEX term expired",
        AlertType.ReworkSla => "Internal rework deadline (MFO)",
        AlertType.ExportSla => "Internal export deadline",
        AlertType.HseSla => "Internal HSE confirmation deadline",
        AlertType.QtyMismatch => "Missing pieces",
        _ => "MM60 cost pending",
    };
    public string SeverityText => Severity switch { Severity.Error => "CRITICAL", Severity.Warning => "WARNING", _ => "INFO" };
}

/// <summary>
/// Balance by VS / fiscal year and alert rules. Totals are calculated by the store (SQL on the views with
/// Oracle); the alerts look only at open pallets.
/// </summary>
public sealed class ReportingService
{
    private readonly IProcessQueries _queries;
    private readonly IClock _clock;
    private readonly CalcParameters _prm;

    public ReportingService(IProcessQueries queries, IClock clock, CalcParameters? parameters = null)
    {
        _queries = queries; _clock = clock; _prm = parameters ?? CalcParameters.Default;
    }

    public IReadOnlyList<BalanceRow> BalanceByValueStream(int? fiscalYear = null) => _queries.Balance(fiscalYear);

    public IReadOnlyList<StatusCount> StatusDistribution() => _queries.StatusDistribution();

    public IReadOnlyList<int> FiscalYears() => _queries.FiscalYears();

    public IReadOnlyList<Alert> Alerts()
    {
        var today = _clock.Today;
        var list = new List<Alert>();
        foreach (var p in _queries.OpenPallets().Where(p => !p.Status.IsClosed()))
        {
            var vs = $"VS{p.ValueStreamId}";
            var f = p.Financials;
            if (p.UnitCostUsd is null or 0)
                list.Add(new(AlertType.CostPending, Severity.Info, p.Id, p.FolioCode, vs, $"{p.ReturnedMaterial}: no MM60 cost.", "QMM"));
            if (p.ImportDateElpJup1 is null) continue;
            var days = (today - p.ImportDateElpJup1.Value.Date).Days;
            if (p.LegalDueDate is { } due && f.QtyOpenTotal > 0)
            {
                var left = (due - today).Days;
                if (left < 0) list.Add(new(AlertType.LegalExpired, Severity.Error, p.Id, p.FolioCode, vs, $"IMMEX term expired {-left} days ago (USD {f.BalanceGeneralUsd:N2}).", "OSD + QMM"));
                else if (left <= _prm.AlertCriticalDaysLeft) list.Add(new(AlertType.AgingCritical, Severity.Error, p.Id, p.FolioCode, vs, $"Expires in {left} days (USD {f.BalanceGeneralUsd:N2}).", "OSD + QMM"));
                else if (left <= _prm.AlertWarningDaysLeft) list.Add(new(AlertType.AgingWarning, Severity.Warning, p.Id, p.FolioCode, vs, $"Expires in {left} days.", "QMM"));
            }
            if (f.QtyOpenFloor > 0 && days > _prm.SlaReworkDays)
                list.Add(new(AlertType.ReworkSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyOpenFloor} pcs on the MFO floor for {days} days.", "MFO"));
            var deliveredOn = p.DateSentBack;
            if (f.QtyPendingExport > 0 && deliveredOn is not null && (today - deliveredOn.Value.Date).Days > _prm.SlaExportDays)
                list.Add(new(AlertType.ExportSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyPendingExport} pcs in LOW without export pedimento for {(today - deliveredOn.Value.Date).Days} days.", "LOW + OSD"));
            if (f.QtyPendingHse > 0 && p.ScrapDate is not null && (today - p.ScrapDate.Value.Date).Days > _prm.SlaHseDays)
                list.Add(new(AlertType.HseSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyPendingHse} {(f.QtyPendingHse == 1 ? "pc" : "pcs")} of scrap without HSE confirmation.", "HSE + MFO"));
            if (f.QtyMissing > 0)
                list.Add(new(AlertType.QtyMismatch, Severity.Error, p.Id, p.FolioCode, vs, $"{(f.QtyMissing == 1 ? "1 pc missing" : $"{f.QtyMissing} pcs missing")} (USD {f.QtyMissing * (p.UnitCostUsd ?? 0):N2}).", "QMM + MFO + LOW"));
        }
        return list.OrderBy(a => a.Severity).ThenBy(a => a.FolioCode).ToList();
    }
}
