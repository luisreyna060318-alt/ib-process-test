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

/// <summary>Alerts screen filters: handled ones too, and only one responsible area (OSD, QMM, MFO, LOW, HSE).</summary>
public sealed record AlertFilter(bool ShowHandled = false, string? Area = null)
{
    public static readonly IReadOnlyList<string> Areas = new[] { "OSD", "QMM", "MFO", "LOW", "HSE" };
    public bool InArea(Alert a) => string.IsNullOrEmpty(Area) || a.Owner.Split(" + ").Contains(Area);
    public bool Keeps(Alert a, bool handled) => (ShowHandled || !handled) && InArea(a);
}

/// <summary>Indicators of the alerts screen (area filter applied): pending critical / warnings, handled, rows of the filter.</summary>
public sealed record AlertTotals(int Critical, int Warnings, int Handled, int Shown);

/// <summary>Alerts calculated by the database (Oracle: SQL on IB_PROCESS_PALLETS_V), one page at a time.</summary>
public interface IAlertQueries
{
    AlertTotals Totals(AlertFilter filter, CalcParameters prm, DateTime today);
    IReadOnlyList<Alert> Page(AlertFilter filter, CalcParameters prm, DateTime today, int skip, int take);
}

/// <summary>Texts and order of the alerts, shared by the C# rules and the SQL (both give the same rows).</summary>
public static class AlertTexts
{
    /// <summary>Groups of the screen, in this order (responsible areas).</summary>
    public static readonly IReadOnlyList<string> OwnerOrder = new[] { "OSD + QMM", "QMM", "QMM + MFO + LOW", "MFO", "HSE + MFO", "LOW + OSD" };
    public static int OwnerRank(string owner) { var i = OwnerOrder.ToList().IndexOf(owner); return i < 0 ? 99 : i; }

    public static string Owner(AlertType t) => t switch
    {
        AlertType.CostPending => "QMM", AlertType.LegalExpired or AlertType.AgingCritical => "OSD + QMM", AlertType.AgingWarning => "QMM",
        AlertType.ReworkSla => "MFO", AlertType.ExportSla => "LOW + OSD", AlertType.HseSla => "HSE + MFO", _ => "QMM + MFO + LOW",
    };

    public static Severity SeverityOf(AlertType t) => t switch
    {
        AlertType.CostPending => Severity.Info,
        AlertType.LegalExpired or AlertType.AgingCritical or AlertType.QtyMismatch => Severity.Error,
        _ => Severity.Warning,
    };

    /// <summary>Message of an alert from its numbers (n1, n2: pieces / days; usd: amount).</summary>
    public static string Message(AlertType t, int n1, int n2, decimal usd, string? material) => t switch
    {
        AlertType.CostPending => $"{material}: no MM60 cost.",
        AlertType.LegalExpired => $"IMMEX term expired {n1} days ago (USD {usd:N2}).",
        AlertType.AgingCritical => $"Expires in {n1} days (USD {usd:N2}).",
        AlertType.AgingWarning => $"Expires in {n1} days.",
        AlertType.ReworkSla => $"{n1} pcs on the MFO floor for {n2} days.",
        AlertType.ExportSla => $"{n1} pcs in LOW without export pedimento for {n2} days.",
        AlertType.HseSla => $"{n1} {(n1 == 1 ? "pc" : "pcs")} of scrap without HSE confirmation.",
        _ => $"{(n1 == 1 ? "1 pc missing" : $"{n1} pcs missing")} (USD {usd:N2}).",
    };

    public static Alert Make(AlertType t, int palletId, string folio, int vs, int n1, int n2, decimal usd, string? material) =>
        new(t, SeverityOf(t), palletId, folio, $"VS{vs}", Message(t, n1, n2, usd, material), Owner(t));

    /// <summary>Order of the screen and of the pages: area group, severity, folio, type.</summary>
    public static IOrderedEnumerable<Alert> Ordered(IEnumerable<Alert> alerts) =>
        alerts.OrderBy(a => OwnerRank(a.Owner)).ThenBy(a => a.Severity).ThenBy(a => a.FolioCode, StringComparer.Ordinal).ThenBy(a => a.Type);
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

    private readonly IAlertQueries? _alertQueries;

    public ReportingService(IProcessQueries queries, IClock clock, CalcParameters? parameters = null, IAlertQueries? alertQueries = null)
    {
        _queries = queries; _clock = clock; _prm = parameters ?? CalcParameters.Default; _alertQueries = alertQueries;
    }

    /// <summary>Alerts screen indicators. With Oracle the database counts; otherwise the C# rules (<paramref name="handled"/> = acknowledged).</summary>
    public AlertTotals AlertTotals(AlertFilter filter, Func<int, AlertType, bool> handled)
    {
        if (_alertQueries is not null) return _alertQueries.Totals(filter, _prm, _clock.Today);
        var area = Alerts().Where(filter.InArea).ToList();
        var pending = area.Where(a => !handled(a.PalletId, a.Type)).ToList();
        return new AlertTotals(pending.Count(a => a.Severity == Severity.Error), pending.Count(a => a.Severity == Severity.Warning),
            area.Count - pending.Count, filter.ShowHandled ? area.Count : pending.Count);
    }

    /// <summary>One page of the alerts screen, in the order of <see cref="AlertTexts.Ordered"/>.</summary>
    public IReadOnlyList<Alert> AlertPage(AlertFilter filter, Func<int, AlertType, bool> handled, int skip, int take)
    {
        if (_alertQueries is not null) return _alertQueries.Page(filter, _prm, _clock.Today, skip, take);
        return AlertTexts.Ordered(Alerts().Where(a => filter.Keeps(a, handled(a.PalletId, a.Type))))
            .Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).ToList();
    }

    public IReadOnlyList<BalanceRow> BalanceByValueStream(int? fiscalYear = null) => _queries.Balance(fiscalYear);

    public IReadOnlyList<StatusCount> StatusDistribution() => _queries.StatusDistribution();

    public IReadOnlyList<int> FiscalYears() => _queries.FiscalYears();

    /// <summary>Every alert of the open pallets, calculated in C# (reference of the SQL of OracleAlertQueries, and tests).</summary>
    public IReadOnlyList<Alert> Alerts()
    {
        var today = _clock.Today;
        var list = new List<Alert>();
        foreach (var p in _queries.OpenPallets().Where(p => !p.Status.IsClosed()))
        {
            var f = p.Financials;
            void Add(AlertType t, int n1 = 0, int n2 = 0, decimal usd = 0m) =>
                list.Add(AlertTexts.Make(t, p.Id, p.FolioCode, p.ValueStreamId, n1, n2, usd, p.ReturnedMaterial));
            if (p.UnitCostUsd is null or 0) Add(AlertType.CostPending);
            if (p.ImportDateElpJup1 is null) continue;
            var days = (today - p.ImportDateElpJup1.Value.Date).Days;
            if (p.LegalDueDate is { } due && f.QtyOpenTotal > 0)
            {
                var left = (due.Date - today).Days;
                if (left < 0) Add(AlertType.LegalExpired, -left, usd: f.BalanceGeneralUsd);
                else if (left <= _prm.AlertCriticalDaysLeft) Add(AlertType.AgingCritical, left, usd: f.BalanceGeneralUsd);
                else if (left <= _prm.AlertWarningDaysLeft) Add(AlertType.AgingWarning, left);
            }
            if (f.QtyOpenFloor > 0 && days > _prm.SlaReworkDays) Add(AlertType.ReworkSla, f.QtyOpenFloor, days);
            if (f.QtyPendingExport > 0 && p.DateSentBack is { } sent && (today - sent.Date).Days > _prm.SlaExportDays)
                Add(AlertType.ExportSla, f.QtyPendingExport, (today - sent.Date).Days);
            if (f.QtyPendingHse > 0 && p.ScrapDate is { } scrap && (today - scrap.Date).Days > _prm.SlaHseDays) Add(AlertType.HseSla, f.QtyPendingHse);
            if (f.QtyMissing > 0) Add(AlertType.QtyMismatch, f.QtyMissing, usd: f.QtyMissing * (p.UnitCostUsd ?? 0));
        }
        return AlertTexts.Ordered(list).ToList();
    }
}
