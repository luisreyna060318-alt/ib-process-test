namespace ExpoReturn.Domain;

/// <summary>Parámetros versionados (cfg.Parameters). Valores provisionales [VALIDAR GS/OSD43].</summary>
public sealed record CalcParameters
{
    public decimal DutyRate { get; init; } = 0.19m;
    public int ImmexLegalTermDays { get; init; } = 540;
    public int AlertWarningDaysLeft { get; init; } = 60;
    public int AlertCriticalDaysLeft { get; init; } = 30;
    public int QtyTolerancePcs { get; init; } = 0;
    public int SlaReworkDays { get; init; } = 15;
    public int SlaExportDays { get; init; } = 7;
    public int SlaHseDays { get; init; } = 10;

    public static CalcParameters Default { get; } = new();
}

/// <summary>Cantidades de conciliación e indicadores USD (Etapa 5).</summary>
public sealed record ReconciliationResult(
    int QtyOpenFloor, int QtyMissing, int QtyPendingExport, int QtyPendingHse,
    decimal MfoInventoryUsd, decimal HseScrapUsd, decimal MfoLowFailureUsd,
    decimal BalanceGeneralUsd, decimal DutyExposureUsd, decimal DutiesToPayUsd)
{
    public static readonly ReconciliationResult Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public int QtyOpenTotal => QtyOpenFloor + QtyMissing + QtyPendingExport + QtyPendingHse;
}

/// <summary>
/// Motor financiero CalcVersion 2 (doc 03 §3.2). Corrige los defectos del Excel:
/// faltantes que desaparecían (H-03), scrap liberado sólo con confirmación HSE (H-04),
/// y cierre aduanal sólo con documento reconocido (H-02).
/// </summary>
public static class FinancialCalculator
{
    public static ReconciliationResult Calculate(PalletReturn p, CalcParameters prm)
    {
        if (p.ImportDateElpJup1 is null || p.IsCancelled) return ReconciliationResult.Zero;

        var cost = p.UnitCostUsd ?? 0m;
        var old = p.OldMaterialDeliveredQty ?? 0;
        var scrap = p.ScrapQty ?? 0;
        var remainder = Math.Max(0, p.QtyImported - old - scrap);
        var mfoClosed = p.PhysicalDeliveredQty is not null;
        var exported = ClosureDocument.Closes(p.ClosureType);
        var scrapConfirmed = p.HseConfirmationDate is not null && !string.IsNullOrWhiteSpace(p.ScrapGrNumber) && p.HseScrapKg > 0;

        var openFloor = mfoClosed ? 0 : remainder;
        var missing = mfoClosed ? remainder : 0;
        var pendingExport = exported ? 0 : old;
        var pendingHse = scrapConfirmed ? 0 : scrap;

        var inv = R(openFloor * cost);
        var hse = R(pendingHse * cost);
        var fail = R((pendingExport + missing) * cost);
        var bal = inv + hse + fail;
        return new ReconciliationResult(openFloor, missing, pendingExport, pendingHse,
            inv, hse, fail, bal, R(bal * prm.DutyRate), R(fail * prm.DutyRate));
    }

    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
