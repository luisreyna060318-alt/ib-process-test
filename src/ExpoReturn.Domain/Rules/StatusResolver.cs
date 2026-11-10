namespace ExpoReturn.Domain;

public sealed record DutyRiskAssessment(bool IsAtRisk, string? Reason, DateTime? LegalDueDate, int? DaysToLegalDue);

public static class StageGuards
{
    public static bool Stage1Complete(PalletReturn p) =>
        p.ValueStreamId > 0
        && !string.IsNullOrWhiteSpace(p.QmmResponsible)
        && !string.IsNullOrWhiteSpace(p.RequestingDepartment)
        && !string.IsNullOrWhiteSpace(p.RequestorName)
        && p.ReasonId is not null
        && !string.IsNullOrWhiteSpace(p.ReturnedMaterial)
        && p.QtyImported > 0;

    public static bool Stage3Started(PalletReturn p) =>
        !string.IsNullOrWhiteSpace(p.NewMaterial) || !string.IsNullOrWhiteSpace(p.NewHU)
        || p.ReinsertionQty is not null || (p.ScrapQty ?? 0) > 0 || p.DateSentBack is not null;
}

/// <summary>Reglas DR-1..DR-4 (doc 03 §5.1).</summary>
public static class DutyRisk
{
    public static DutyRiskAssessment Evaluate(PalletReturn p, ReconciliationResult r, DateTime today, CalcParameters prm)
    {
        if (p.ImportDateElpJup1 is null) return new(false, null, null, null);
        var due = p.ImportDateElpJup1.Value.Date.AddDays(prm.ImmexLegalTermDays);
        var daysLeft = (int)(due - today.Date).TotalDays;
        if (r.QtyOpenTotal == 0) return new(false, null, due, null);

        if (daysLeft < 0) return new(true, $"IMMEX term expired {-daysLeft} days ago", due, daysLeft);
        if (daysLeft <= prm.AlertCriticalDaysLeft) return new(true, $"IMMEX term expires in {daysLeft} days", due, daysLeft);
        if (r.QtyMissing > prm.QtyTolerancePcs) return new(true, $"{r.QtyMissing} pcs missing without evidence", due, daysLeft);
        var cat = Catalogs.FindCategory(p.StatusCategoryCode);
        if (cat is { AffectsDuty: true }) return new(true, $"Discrepancy: {cat.Description}", due, daysLeft);
        return new(false, null, due, daysLeft);
    }
}

/// <summary>Máquina de estados derivada (doc 03 §4.1).</summary>
public static class StatusResolver
{
    public static WorkflowStatus Resolve(PalletReturn p, ReconciliationResult r, DutyRiskAssessment risk)
    {
        // Histórico: el Excel nunca capturó motivo ni departamento; el estatus se deriva de los datos operativos.
        if (!p.IsLegacy && !StageGuards.Stage1Complete(p)) return WorkflowStatus.Draft;
        if (p.ImportDateElpJup1 is null) return WorkflowStatus.Requested;

        if (r.QtyOpenTotal == 0)
        {
            if (p.ClosureType == ClosureDocumentType.CambioRegimenF4) return WorkflowStatus.ClosedDutyPaid;
            return (p.OldMaterialDeliveredQty ?? 0) == 0 && (p.ScrapQty ?? 0) > 0 ? WorkflowStatus.Scrapped : WorkflowStatus.Exported;
        }

        if (risk.IsAtRisk) return WorkflowStatus.DutyRisk;
        if (r.QtyOpenFloor > 0) return StageGuards.Stage3Started(p) ? WorkflowStatus.InRework : WorkflowStatus.Imported;
        if (r.QtyPendingExport > 0) return WorkflowStatus.PendingExport;
        return WorkflowStatus.PendingHse;
    }

    /// <summary>Categoría automática (OUTCOME); las DISCREPANCY las asigna QMM.</summary>
    public static string? AutoCategory(WorkflowStatus s, ReconciliationResult r) => s switch
    {
        WorkflowStatus.Exported => "EXPORT",
        WorkflowStatus.Scrapped => "SCRAP",
        _ when r.QtyOpenFloor > 0 => "INVENTORY",
        _ => null,
    };
}
