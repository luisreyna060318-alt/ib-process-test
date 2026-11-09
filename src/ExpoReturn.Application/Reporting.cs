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
        AlertType.AgingWarning => "Antigüedad IMMEX (aviso)",
        AlertType.AgingCritical => "Antigüedad IMMEX (crítica)",
        AlertType.LegalExpired => "Plazo IMMEX vencido",
        AlertType.ReworkSla => "Plazo interno de retrabajo (MFO)",
        AlertType.ExportSla => "Plazo interno de exportación",
        AlertType.HseSla => "Plazo interno de confirmación HSE",
        AlertType.QtyMismatch => "Faltante de piezas",
        _ => "Costo MM60 pendiente",
    };
    public string SeverityText => Severity switch { Severity.Error => "CRÍTICA", Severity.Warning => "AVISO", _ => "INFO" };
}

/// <summary>Equivalente a rpt.vw_Balance_ByVS_Period y al Worker de alertas (doc 03 §5).</summary>
public sealed class ReportingService
{
    private readonly IPalletReturnRepository _repo;
    private readonly IClock _clock;
    private readonly CalcParameters _prm = CalcParameters.Default;

    public ReportingService(IPalletReturnRepository repo, IClock clock) { _repo = repo; _clock = clock; }

    public IReadOnlyList<BalanceRow> BalanceByValueStream(int? fiscalYear = null)
    {
        var rows = _repo.GetAll()
            .Where(p => p.ImportDateElpJup1 is not null)
            .Where(p => fiscalYear is null || p.ImportDateElpJup1!.Value.Year == fiscalYear)
            .GroupBy(p => p.ValueStreamId)
            .OrderBy(g => g.Key)
            .Select(g => Row($"VS{g.Key}", g.ToList()))
            .ToList();
        if (rows.Count > 0)
            rows.Add(Row("Total", _repo.GetAll().Where(p => p.ImportDateElpJup1 is not null
                                                       && (fiscalYear is null || p.ImportDateElpJup1!.Value.Year == fiscalYear)).ToList()));
        return rows;
    }

    private static BalanceRow Row(string vs, List<PalletReturn> list)
    {
        var total = list.Sum(p => p.SapUsdTotal);
        var inv = list.Sum(p => p.Financials.MfoInventoryUsd);
        var hse = list.Sum(p => p.Financials.HseScrapUsd);
        var fail = list.Sum(p => p.Financials.MfoLowFailureUsd);
        return new BalanceRow(vs, list.Count, list.Count(p => !p.Status.IsClosed()), list.Count(p => p.IsDutyRisk),
            total, inv, hse, fail, total - inv - hse - fail, inv + hse + fail,
            list.Sum(p => p.Financials.DutiesToPayUsd));
    }

    public IReadOnlyList<StatusCount> StatusDistribution() =>
        _repo.GetAll().GroupBy(p => p.Status).OrderBy(g => g.Key).Select(g => new StatusCount(g.Key, g.Count())).ToList();

    public IReadOnlyList<int> FiscalYears() =>
        _repo.GetAll().Where(p => p.ImportDateElpJup1 is not null).Select(p => p.ImportDateElpJup1!.Value.Year).Distinct().OrderBy(y => y).ToList();

    public IReadOnlyList<Alert> Alerts()
    {
        var today = _clock.Today;
        var list = new List<Alert>();
        foreach (var p in _repo.GetAll().Where(p => !p.Status.IsClosed()))
        {
            var vs = $"VS{p.ValueStreamId}";
            var f = p.Financials;
            if (p.UnitCostUsd is null or 0)
                list.Add(new(AlertType.CostPending, Severity.Info, p.Id, p.FolioCode, vs, $"{p.ReturnedMaterial}: sin costo MM60.", "QMM"));
            if (p.ImportDateElpJup1 is null) continue;
            var days = (today - p.ImportDateElpJup1.Value.Date).Days;
            if (p.LegalDueDate is { } due && f.QtyOpenTotal > 0)
            {
                var left = (due - today).Days;
                if (left < 0) list.Add(new(AlertType.LegalExpired, Severity.Error, p.Id, p.FolioCode, vs, $"Plazo IMMEX vencido hace {-left} días (USD {f.BalanceGeneralUsd:N2}).", "GS/OSD43 + QMM"));
                else if (left <= _prm.AlertCriticalDaysLeft) list.Add(new(AlertType.AgingCritical, Severity.Error, p.Id, p.FolioCode, vs, $"Vence en {left} días (USD {f.BalanceGeneralUsd:N2}).", "GS/OSD43 + QMM"));
                else if (left <= _prm.AlertWarningDaysLeft) list.Add(new(AlertType.AgingWarning, Severity.Warning, p.Id, p.FolioCode, vs, $"Vence en {left} días.", "QMM"));
            }
            if (f.QtyOpenFloor > 0 && days > _prm.SlaReworkDays)
                list.Add(new(AlertType.ReworkSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyOpenFloor} pzs en piso MFO desde hace {days} días.", "MFO"));
            var deliveredOn = p.DateSentBack;
            if (f.QtyPendingExport > 0 && deliveredOn is not null && (today - deliveredOn.Value.Date).Days > _prm.SlaExportDays)
                list.Add(new(AlertType.ExportSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyPendingExport} pzs en LOW sin pedimento desde hace {(today - deliveredOn.Value.Date).Days} días.", "LOW + GS"));
            if (f.QtyPendingHse > 0 && p.ScrapDate is not null && (today - p.ScrapDate.Value.Date).Days > _prm.SlaHseDays)
                list.Add(new(AlertType.HseSla, Severity.Warning, p.Id, p.FolioCode, vs, $"{f.QtyPendingHse} {(f.QtyPendingHse == 1 ? "pz" : "pzs")} de scrap sin confirmación HSE.", "HSE + MFO"));
            if (f.QtyMissing > 0)
                list.Add(new(AlertType.QtyMismatch, Severity.Error, p.Id, p.FolioCode, vs, $"{(f.QtyMissing == 1 ? "Falta 1 pz" : $"Faltan {f.QtyMissing} pzs")} (USD {f.QtyMissing * (p.UnitCostUsd ?? 0):N2}).", "QMM + MFO + LOW"));
        }
        return list.OrderBy(a => a.Severity).ThenBy(a => a.FolioCode).ToList();
    }
}
