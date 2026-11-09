using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Domain;

public sealed record ValidationIssue(string Code, Severity Severity, string Field, string Message)
{
    private static readonly HashSet<string> RequiredCodes = new() { "VAL-001", "VAL-012", "VAL-015", "VAL-016", "VAL-017" };

    public string SeverityText => Severity switch { Severity.Error => "Error", Severity.Warning => "Advertencia", _ => "Info" };

    /// <summary>Aviso de dato obligatorio pendiente para pasar a "Solicitado" (no bloquea el guardado).</summary>
    public bool IsRequiredPending => Severity == Severity.Warning && RequiredCodes.Contains(Code);
}

/// <summary>Catálogo de validaciones VAL-xxx (doc 03 §2), ajustado a lo observado en el Excel real.</summary>
public static class PalletValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(PalletReturn p, DateTime today)
    {
        var v = new List<ValidationIssue>();
        void E(string code, string field, string msg) => v.Add(new(code, Severity.Error, field, msg));
        void W(string code, string field, string msg) => v.Add(new(code, Severity.Warning, field, msg));

        // ---- Transversales ----
        foreach (var (field, date) in Dates(p))
            if (date?.Date > today.Date) E("VAL-003", field, $"{field}: no se permiten fechas futuras.");

        // ---- Etapa 1 ----
        if (string.IsNullOrWhiteSpace(p.ReturnedMaterial)) W("VAL-001", nameof(p.ReturnedMaterial), "Falta Returned Pallet 13 Digits Part number (requerido para pasar a Solicitado).");
        else if (!Identifiers.IsValidMaterial(p.ReturnedMaterial)) E("VAL-010", nameof(p.ReturnedMaterial), "P/N no cumple el formato XXXX.XXX.XXX-XX(X).");
        if (p.UnitCostUsd is null or 0) W("VAL-011", nameof(p.UnitCostUsd), "Material sin MM60 SAP UNIT COST ('COSTEAR'): el valor en riesgo se calcula en 0.");
        if (p.QtyImported < 0) E("VAL-012", nameof(p.QtyImported), "Quantity imported no puede ser negativa.");
        else if (p.QtyImported == 0) W("VAL-012", nameof(p.QtyImported), "Capture Quantity imported (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.OriginalHU) && string.IsNullOrWhiteSpace(p.OriginalBatch))
            W("VAL-001", nameof(p.OriginalHU), "Capture Original #HU u Original Batch.");
        if (!string.IsNullOrWhiteSpace(p.OriginalHU) && !Identifiers.IsHandlingUnit(p.OriginalHU))
            W("VAL-013", nameof(p.OriginalHU), "Original #HU no es de 10 dígitos (si es un lote, captúrelo en Original Batch). [VALIDAR regla]");
        if (!string.IsNullOrWhiteSpace(p.OriginalBatch) && !Identifiers.IsBatch(p.OriginalBatch))
            W("VAL-014", nameof(p.OriginalBatch), "Original Batch no tiene el formato AA99999999.");
        if (p.ReasonId is null) W("VAL-015", nameof(p.ReasonId), "Seleccione Reason (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.RequestingDepartment)) W("VAL-016", nameof(p.RequestingDepartment), "Seleccione Requesting department (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.RequestorName)) W("VAL-001", nameof(p.RequestorName), "Capture Requestor (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.QmmResponsible)) W("VAL-017", nameof(p.QmmResponsible), "Asigne el QMM Responsible (Champion/Proxy del VS) para pasar a Solicitado.");

        // ---- Etapa 2 ----
        var stage2Data = p.ImportDate is not null || p.ImportDateElpJup1 is not null || !string.IsNullOrWhiteSpace(p.ImportInvoice);
        if (stage2Data && !StageGuards.Stage1Complete(p)) E("VAL-020", nameof(p.ImportDateElpJup1), "Complete la Solicitud (pestaña 1) antes de registrar la importación.");
        if (p.ImportDate < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDate), "Import Date es anterior a Date PO. [VALIDAR regla]");
        if (p.ImportDateElpJup1 < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDateElpJup1), "Import date ELP-JuP1 es anterior a Date PO. [VALIDAR regla]");
        if (p.ImportDateElpJup1 is not null && (p.ImportDate is null || string.IsNullOrWhiteSpace(p.ImportInvoice)))
            E("VAL-022", nameof(p.ImportDateElpJup1), "Import date ELP-JuP1 requiere Import Date e Invoice.");
        if (!string.IsNullOrWhiteSpace(p.ImportInvoice))
        {
            if (!Identifiers.IsInvoice(p.ImportInvoice)) W("VAL-023", nameof(p.ImportInvoice), "Invoice no cumple el formato MPS########.");
            else if (p.ImportDate is not null && Identifiers.InvoiceYear(p.ImportInvoice) != p.ImportDate.Value.Year)
                W("VAL-023", nameof(p.ImportInvoice), $"El año de Invoice ({Identifiers.InvoiceYear(p.ImportInvoice)}) no coincide con Import Date ({p.ImportDate:yyyy}).");
        }
        if (!string.IsNullOrWhiteSpace(p.ReturnPONumber) && !Identifiers.IsPurchaseOrder(p.ReturnPONumber))
            W("VAL-024", nameof(p.ReturnPONumber), "PO Number debe tener 8 dígitos (opcionalmente ' - posición', p. ej. 49398082 - 999).");

        // ---- Etapa 3 ----
        var stage3Data = p.PhysicalDeliveredQty is not null || p.OldMaterialDeliveredQty is not null || (p.ScrapQty ?? 0) > 0 || p.NewHU is not null;
        if (stage3Data && p.ImportDateElpJup1 is null) E("VAL-030", nameof(p.PhysicalDeliveredQty), "No se puede registrar la ejecución MFO sin Import date ELP-JuP1.");
        var reason = Catalogs.FindReason(p.ReasonId);
        if (reason is { RequiresNewMaterial: true })
        {
            if (stage3Data && string.IsNullOrWhiteSpace(p.NewMaterial)) E("VAL-031", nameof(p.NewMaterial), "Reflash: capture Returned Pallet 13 Dgt New PN.");
            else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial == p.ReturnedMaterial) E("VAL-031", nameof(p.NewMaterial), "Reflash: New PN debe ser distinto al número de parte original.");
        }
        else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial != p.ReturnedMaterial)
            W("VAL-031", nameof(p.NewMaterial), "New PN es distinto al original pero Reason no es 'reflash'.");
        if (!string.IsNullOrWhiteSpace(p.NewMaterial) && !Identifiers.IsValidMaterial(p.NewMaterial))
            E("VAL-010", nameof(p.NewMaterial), "New PN no cumple el formato XXXX.XXX.XXX-XX(X).");
        if (p.PhysicalDeliveredQty is not null && p.OldMaterialDeliveredQty is not null
            && p.PhysicalDeliveredQty != p.OldMaterialDeliveredQty + (p.ReinsertionQty ?? 0))
            E("VAL-032", nameof(p.PhysicalDeliveredQty), "Physical material delivered to LOW debe ser igual a Old Material Delivered to LOW + New Reinsertion Material Qty.");
        var outQty = (p.OldMaterialDeliveredQty ?? 0) + (p.ScrapQty ?? 0);
        if (outQty > p.QtyImported) E("VAL-033", nameof(p.OldMaterialDeliveredQty), $"Old Material Delivered to LOW + Scrap to HSE ({outQty}) excede Quantity imported ({p.QtyImported}).");
        if (p.PhysicalDeliveredQty is not null && outQty < p.QtyImported)
            W("VAL-034", nameof(p.PhysicalDeliveredQty), $"Faltan {p.QtyImported - outQty} pzs por conciliar: quedan como faltante y el pallet pasa a riesgo arancelario. QMM debe clasificarlo en COMMENTS.");
        if ((p.ScrapQty ?? 0) > 0 && p.ScrapDate is null) E("VAL-035", nameof(p.ScrapDate), "Scrap to HSE (Qty) mayor a 0 requiere Scrap Date.");
        if (p.ScrapDate < p.ImportDateElpJup1) E("VAL-035", nameof(p.ScrapDate), "Scrap Date no puede ser anterior a Import date ELP-JuP1.");
        if (!string.IsNullOrWhiteSpace(p.NewHU) && !Identifiers.IsHandlingUnit(p.NewHU))
            W("VAL-036", nameof(p.NewHU), "New HU no es de 10 dígitos. [VALIDAR regla]");
        if (p.DateSentBack < p.ImportDateElpJup1) E("VAL-037", nameof(p.DateSentBack), "Date send Back no puede ser anterior a Import date ELP-JuP1.");

        // ---- Etapa 4 ----
        if (!string.IsNullOrWhiteSpace(p.LowExport) && (p.OldMaterialDeliveredQty ?? 0) == 0)
            W("VAL-040", nameof(p.LowExport), "LOW export capturado sin Old Material Delivered to LOW.");
        var hseAny = p.HseConfirmationDate is not null || p.HseScrapKg is not null || !string.IsNullOrWhiteSpace(p.ScrapGrNumber);
        if (hseAny)
        {
            if ((p.ScrapQty ?? 0) <= 0) E("VAL-041", nameof(p.ScrapQty), "HSE confirmó la destrucción, pero Scrap to HSE (Qty) es 0.");
            if (p.HseConfirmationDate < p.ScrapDate) E("VAL-043", nameof(p.HseConfirmationDate), "HSE confirmation Date no puede ser anterior a Scrap Date.");
            if (!string.IsNullOrWhiteSpace(p.ScrapGrNumber) && !Identifiers.IsGrNumber(p.ScrapGrNumber)) E("VAL-044", nameof(p.ScrapGrNumber), "GR Number debe ser numérico de 10 dígitos.");
            if (p.HseConfirmationDate is not null && (string.IsNullOrWhiteSpace(p.ScrapGrNumber) || p.HseScrapKg is null or <= 0))
                E("VAL-045", nameof(p.HseConfirmationDate), "HSE confirmation Date requiere GR Number y Kg Qty from HSE mayor a 0.");
        }
        switch (p.ClosureType)
        {
            case ClosureDocumentType.Unrecognized:
                E("VAL-046", nameof(p.CustomsExportReference), "Customs Export debe ser PRyy-##### (o F4 / V1). Textos como 'scrap marzo' no cierran el pallet.");
                break;
            case ClosureDocumentType.ExportPedimento when (p.OldMaterialDeliveredQty ?? 0) == 0:
                E("VAL-046", nameof(p.CustomsExportReference), "Customs Export requiere Old Material Delivered to LOW.");
                break;
        }
        return v;
    }

    private static IEnumerable<(string, DateTime?)> Dates(PalletReturn p)
    {
        yield return (nameof(p.DateReturnedToWhs), p.DateReturnedToWhs);
        yield return (nameof(p.ImportDate), p.ImportDate);
        yield return (nameof(p.ImportDateElpJup1), p.ImportDateElpJup1);
        yield return (nameof(p.DateSentBack), p.DateSentBack);
        yield return (nameof(p.ScrapDate), p.ScrapDate);
        yield return (nameof(p.HseConfirmationDate), p.HseConfirmationDate);
    }
}
