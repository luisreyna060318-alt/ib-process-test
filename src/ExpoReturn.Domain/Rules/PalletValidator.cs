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
        if (string.IsNullOrWhiteSpace(p.ReturnedMaterial)) W("VAL-001", nameof(p.ReturnedMaterial), "Falta el número de parte (requerido para pasar a Solicitado).");
        else if (!Identifiers.IsValidMaterial(p.ReturnedMaterial)) E("VAL-010", nameof(p.ReturnedMaterial), "P/N no cumple el formato XXXX.XXX.XXX-XX(X).");
        if (p.UnitCostUsd is null or 0) W("VAL-011", nameof(p.UnitCostUsd), "Material sin costo MM60 (antes 'COSTEAR'): el valor en riesgo se calcula en 0.");
        if (p.QtyImported < 0) E("VAL-012", nameof(p.QtyImported), "La cantidad importada no puede ser negativa.");
        else if (p.QtyImported == 0) W("VAL-012", nameof(p.QtyImported), "Capture la cantidad importada (requerida para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.OriginalHU) && string.IsNullOrWhiteSpace(p.OriginalBatch))
            W("VAL-001", nameof(p.OriginalHU), "Capture el HU original o el lote original.");
        if (!string.IsNullOrWhiteSpace(p.OriginalHU) && !Identifiers.IsHandlingUnit(p.OriginalHU))
            W("VAL-013", nameof(p.OriginalHU), "El HU original no es de 10 dígitos (si es un lote, captúrelo en Lote original). [VALIDAR regla]");
        if (!string.IsNullOrWhiteSpace(p.OriginalBatch) && !Identifiers.IsBatch(p.OriginalBatch))
            W("VAL-014", nameof(p.OriginalBatch), "El lote no tiene el formato AA99999999.");
        if (p.ReasonId is null) W("VAL-015", nameof(p.ReasonId), "Seleccione el motivo oficial (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.RequestingDepartment)) W("VAL-016", nameof(p.RequestingDepartment), "Seleccione el departamento solicitante (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.RequestorName)) W("VAL-001", nameof(p.RequestorName), "Capture el nombre del solicitante (requerido para pasar a Solicitado).");
        if (string.IsNullOrWhiteSpace(p.QmmResponsible)) W("VAL-017", nameof(p.QmmResponsible), "Asigne el QMM Responsible (Champion/Proxy del VS) para pasar a Solicitado.");

        // ---- Etapa 2 ----
        var stage2Data = p.ImportDate is not null || p.ImportDateElpJup1 is not null || !string.IsNullOrWhiteSpace(p.ImportInvoice);
        if (stage2Data && !StageGuards.Stage1Complete(p)) E("VAL-020", nameof(p.ImportDateElpJup1), "Complete la Etapa 1 (solicitud) antes de registrar la importación.");
        if (p.ImportDate < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDate), "La fecha de importación es anterior a la fecha PO. ¿La fecha PO corresponde a la PO de retorno? [VALIDAR regla]");
        if (p.ImportDateElpJup1 < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDateElpJup1), "El cruce El Paso → JuP1 es anterior a la fecha PO. [VALIDAR regla]");
        if (p.ImportDateElpJup1 is not null && (p.ImportDate is null || string.IsNullOrWhiteSpace(p.ImportInvoice)))
            E("VAL-022", nameof(p.ImportDateElpJup1), "El cruce El Paso → JuP1 requiere fecha de importación y factura.");
        if (!string.IsNullOrWhiteSpace(p.ImportInvoice))
        {
            if (!Identifiers.IsInvoice(p.ImportInvoice)) W("VAL-023", nameof(p.ImportInvoice), "La factura no cumple el formato MPS########.");
            else if (p.ImportDate is not null && Identifiers.InvoiceYear(p.ImportInvoice) != p.ImportDate.Value.Year)
                W("VAL-023", nameof(p.ImportInvoice), $"El año de la factura ({Identifiers.InvoiceYear(p.ImportInvoice)}) no coincide con la fecha de importación ({p.ImportDate:yyyy}).");
        }
        if (!string.IsNullOrWhiteSpace(p.ReturnPONumber) && !Identifiers.IsPurchaseOrder(p.ReturnPONumber))
            E("VAL-024", nameof(p.ReturnPONumber), "La PO debe tener 8 dígitos (la posición va en su propio campo).");

        // ---- Etapa 3 ----
        var stage3Data = p.PhysicalDeliveredQty is not null || p.OldMaterialDeliveredQty is not null || (p.ScrapQty ?? 0) > 0 || p.NewHU is not null;
        if (stage3Data && p.ImportDateElpJup1 is null) E("VAL-030", nameof(p.PhysicalDeliveredQty), "No se puede registrar la ejecución MFO sin el cruce El Paso → JuP1.");
        var reason = Catalogs.FindReason(p.ReasonId);
        if (reason is { RequiresNewMaterial: true })
        {
            if (stage3Data && string.IsNullOrWhiteSpace(p.NewMaterial)) E("VAL-031", nameof(p.NewMaterial), "Reflash: capture el nuevo número de parte.");
            else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial == p.ReturnedMaterial) E("VAL-031", nameof(p.NewMaterial), "Reflash: el nuevo P/N debe ser distinto al original.");
        }
        else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial != p.ReturnedMaterial)
            W("VAL-031", nameof(p.NewMaterial), "El P/N nuevo es distinto al original pero el motivo no es 'reflash'.");
        if (!string.IsNullOrWhiteSpace(p.NewMaterial) && !Identifiers.IsValidMaterial(p.NewMaterial))
            E("VAL-010", nameof(p.NewMaterial), "El nuevo P/N no cumple el formato XXXX.XXX.XXX-XX(X).");
        if (p.PhysicalDeliveredQty is not null && p.OldMaterialDeliveredQty is not null
            && p.PhysicalDeliveredQty != p.OldMaterialDeliveredQty + (p.ReinsertionQty ?? 0))
            E("VAL-032", nameof(p.PhysicalDeliveredQty), "Entregado físicamente a LOW debe ser igual a material original devuelto + material reinsertado.");
        var outQty = (p.OldMaterialDeliveredQty ?? 0) + (p.ScrapQty ?? 0);
        if (outQty > p.QtyImported) E("VAL-033", nameof(p.OldMaterialDeliveredQty), $"Material devuelto + scrap ({outQty}) excede lo importado ({p.QtyImported}).");
        if (p.PhysicalDeliveredQty is not null && outQty < p.QtyImported && !p.DeclaredMissing)
            E("VAL-034", nameof(p.PhysicalDeliveredQty), $"Faltan {p.QtyImported - outQty} pzs por conciliar. Corrija material devuelto / scrap o marque 'Declarar faltante'.");
        if (p.DeclaredMissing && Catalogs.FindCategory(p.StatusCategoryCode)?.Kind != "DISCREPANCY")
            E("VAL-050", nameof(p.StatusCategoryCode), "Al declarar faltante, QMM debe asignar una categoría de discrepancia.");
        if ((p.ScrapQty ?? 0) > 0 && p.ScrapDate is null) E("VAL-035", nameof(p.ScrapDate), "Una cantidad a scrap mayor a 0 requiere la fecha de scrap.");
        if (p.ScrapDate < p.ImportDateElpJup1) E("VAL-035", nameof(p.ScrapDate), "La fecha de scrap no puede ser anterior al cruce.");
        if (!string.IsNullOrWhiteSpace(p.NewHU) && !Identifiers.IsHandlingUnit(p.NewHU))
            W("VAL-036", nameof(p.NewHU), "El HU nuevo no es de 10 dígitos. [VALIDAR regla]");
        if (p.DateSentBack < p.ImportDateElpJup1) E("VAL-037", nameof(p.DateSentBack), "La fecha de regreso no puede ser anterior al cruce.");

        // ---- Etapa 4 ----
        if ((p.LowExportNumber is not null || p.LowExportDate is not null) && (p.OldMaterialDeliveredQty ?? 0) == 0)
            E("VAL-040", nameof(p.LowExportNumber), "La expo de LOW requiere material original devuelto a LOW.");
        var hseAny = p.HseConfirmationDate is not null || p.HseScrapKg is not null || !string.IsNullOrWhiteSpace(p.ScrapGrNumber);
        if (hseAny)
        {
            if ((p.ScrapQty ?? 0) <= 0) E("VAL-041", nameof(p.ScrapQty), "HSE confirmó la destrucción, pero la cantidad a scrap es 0.");
            if (p.HseConfirmationDate < p.ScrapDate) E("VAL-043", nameof(p.HseConfirmationDate), "La confirmación HSE no puede ser anterior al scrap.");
            if (!string.IsNullOrWhiteSpace(p.ScrapGrNumber) && !Identifiers.IsGrNumber(p.ScrapGrNumber)) E("VAL-044", nameof(p.ScrapGrNumber), "El número de GR debe ser numérico de 10 dígitos.");
            if (p.HseConfirmationDate is not null && (string.IsNullOrWhiteSpace(p.ScrapGrNumber) || p.HseScrapKg is null or <= 0))
                E("VAL-045", nameof(p.HseConfirmationDate), "La confirmación HSE requiere número de GR y kg mayores a 0.");
        }
        switch (p.ClosureType)
        {
            case ClosureDocumentType.Unrecognized:
                E("VAL-046", nameof(p.CustomsExportReference), "La exportación aduanal debe ser PRyy-##### (o F4 / V1). Textos como 'scrap marzo' no cierran el pallet.");
                break;
            case ClosureDocumentType.ExportPedimento when (p.OldMaterialDeliveredQty ?? 0) == 0:
                E("VAL-046", nameof(p.CustomsExportReference), "La exportación requiere material original devuelto a LOW.");
                break;
        }
        if (p.CustomsExportDate < p.LowExportDate) E("VAL-046", nameof(p.CustomsExportDate), "La exportación aduanal no puede ser anterior a la expo de LOW.");
        if (p.IsCancelled && p.ImportDateElpJup1 is not null) E("VAL-052", nameof(p.IsCancelled), "No se puede cancelar un pallet que ya cruzó: debe conciliarse.");
        if (p.IsCancelled && string.IsNullOrWhiteSpace(p.CancelReason)) E("VAL-001", nameof(p.CancelReason), "Capture el motivo de cancelación.");
        return v;
    }

    private static IEnumerable<(string, DateTime?)> Dates(PalletReturn p)
    {
        yield return (nameof(p.DateReturnedToWhs), p.DateReturnedToWhs);
        yield return (nameof(p.ImportDate), p.ImportDate);
        yield return (nameof(p.ImportDateElpJup1), p.ImportDateElpJup1);
        yield return (nameof(p.PhysicalDeliveredDate), p.PhysicalDeliveredDate);
        yield return (nameof(p.DateSentBack), p.DateSentBack);
        yield return (nameof(p.ScrapDate), p.ScrapDate);
        yield return (nameof(p.LowExportDate), p.LowExportDate);
        yield return (nameof(p.CustomsExportDate), p.CustomsExportDate);
        yield return (nameof(p.HseConfirmationDate), p.HseConfirmationDate);
    }
}
