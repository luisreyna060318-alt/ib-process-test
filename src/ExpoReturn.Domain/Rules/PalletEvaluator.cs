namespace ExpoReturn.Domain;

public sealed record PalletEvaluation(
    ReconciliationResult Financials, WorkflowStatus Status, DutyRiskAssessment Risk,
    string? AutoCategory, IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);
}

/// <summary>Punto único de cálculo: indicadores + estatus + validaciones. Sin efectos secundarios.</summary>
public static class PalletEvaluator
{
    public static PalletEvaluation Evaluate(PalletReturn p, CalcParameters prm, DateTime today)
    {
        var fin = FinancialCalculator.Calculate(p, prm);
        var risk = DutyRisk.Evaluate(p, fin, today, prm);
        var status = StatusResolver.Resolve(p, fin, risk);
        var issues = p.IsLegacy ? Array.Empty<ValidationIssue>() : PalletValidator.Validate(p, today);
        return new PalletEvaluation(fin, status, risk, StatusResolver.AutoCategory(status, fin), issues);
    }

    /// <summary>Calculates every pallet of a process and tells the screens the totals changed.</summary>
    public static void Apply(IbProcess process, CalcParameters prm, DateTime today)
    {
        foreach (var p in process.Pallets) Apply(p, prm, today);
        process.OpenFindings = process.IsLegacy ? PalletValidator.ValidateProcess(process, today).Count : 0;
        process.RaiseTotals();
    }

    /// <summary>Escribe los resultados calculados en el registro (al guardar o al migrar).</summary>
    public static PalletEvaluation Apply(PalletReturn p, CalcParameters prm, DateTime today)
    {
        var e = Evaluate(p, prm, today);
        p.Financials = e.Financials;
        p.Status = e.Status;
        p.IsDutyRisk = e.Risk.IsAtRisk;
        p.DutyRiskReason = e.Risk.Reason;
        p.LegalDueDate = e.Risk.LegalDueDate;
        var current = Catalogs.FindCategory(p.StatusCategoryCode);
        if (e.AutoCategory is not null && (current is null || current.IsAutoDerived)) p.StatusCategoryCode = e.AutoCategory;
        return e;
    }
}
