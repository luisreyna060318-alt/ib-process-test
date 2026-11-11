using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Domain;

/// <param name="FromExcel">Problem the record already had when it was imported from the Excel 2.0 (to homogenize; never blocks).</param>
public sealed record ValidationIssue(string Code, Severity Severity, string Field, string Message, bool FromExcel = false)
{
    private static readonly HashSet<string> RequiredCodes = new() { "VAL-001", "VAL-012", "VAL-015", "VAL-016", "VAL-017" };

    public string SeverityText => Severity switch { Severity.Error => "Error", Severity.Warning => "Warning", _ => "Info" };

    /// <summary>Aviso de dato obligatorio pendiente para pasar a "Solicitado" (no bloquea el guardado).</summary>
    public bool IsRequiredPending => !FromExcel && Severity == Severity.Warning && RequiredCodes.Contains(Code);
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
            if (date?.Date > today.Date) E("VAL-003", field, $"{field}: future dates are not allowed.");

        // ---- Etapa 1 ----
        if (string.IsNullOrWhiteSpace(p.ReturnedMaterial)) W("VAL-001", nameof(p.ReturnedMaterial), "Returned part number is missing (required to move to Requested).");
        else if (!Identifiers.IsValidMaterial(p.ReturnedMaterial)) E("VAL-010", nameof(p.ReturnedMaterial), "Part number does not match XXXX.XXX.XXX-XX(X).");
        if (p.UnitCostUsd is null or 0) W("VAL-011", nameof(p.UnitCostUsd), "No MM60 cost for this material ('COSTEAR'): the value at risk is calculated as 0.");
        if (p.QtyImported < 0) E("VAL-012", nameof(p.QtyImported), "Quantity imported cannot be negative.");
        else if (p.QtyImported == 0) W("VAL-012", nameof(p.QtyImported), "Enter the quantity imported (required to move to Requested).");
        if (string.IsNullOrWhiteSpace(p.OriginalHU) && string.IsNullOrWhiteSpace(p.OriginalBatch))
            W("VAL-001", nameof(p.OriginalHU), "Enter the original HU or the original batch.");
        if (!string.IsNullOrWhiteSpace(p.OriginalHU) && !Identifiers.IsHandlingUnit(p.OriginalHU))
            W("VAL-013", nameof(p.OriginalHU), "Original HU is not 10 digits (if it is a batch, enter it in Original batch). [VALIDATE rule]");
        if (!string.IsNullOrWhiteSpace(p.OriginalBatch) && !Identifiers.IsBatch(p.OriginalBatch))
            W("VAL-014", nameof(p.OriginalBatch), "Original batch does not match AA99999999.");
        if (p.ReasonId is null) W("VAL-015", nameof(p.ReasonId), "Select the reason (required to move to Requested).");
        if (string.IsNullOrWhiteSpace(p.RequestingDepartment)) W("VAL-016", nameof(p.RequestingDepartment), "Select the requesting department (required to move to Requested).");
        if (string.IsNullOrWhiteSpace(p.RequestorName)) W("VAL-001", nameof(p.RequestorName), "Enter the requestor name (required to move to Requested).");
        if (string.IsNullOrWhiteSpace(p.QmmResponsible)) W("VAL-017", nameof(p.QmmResponsible), "Assign the QMM responsible (VS Champion/Proxy) to move to Requested.");

        // ---- Etapa 2 ----
        var stage2Data = p.ImportDate is not null || p.ImportDateElpJup1 is not null || !string.IsNullOrWhiteSpace(p.ImportInvoice);
        if (stage2Data && !StageGuards.Stage1Complete(p)) E("VAL-020", nameof(p.ImportDateElpJup1), "Complete the request (tab 1) before recording the import.");
        if (p.ImportDate < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDate), "Import date is earlier than the PO date. [VALIDATE rule]");
        if (p.ImportDateElpJup1 < p.DateReturnedToWhs) W("VAL-021", nameof(p.ImportDateElpJup1), "El Paso → JuP1 crossing is earlier than the PO date. [VALIDATE rule]");
        if (p.ImportDateElpJup1 is not null && (p.ImportDate is null || string.IsNullOrWhiteSpace(p.ImportInvoice)))
            E("VAL-022", nameof(p.ImportDateElpJup1), "El Paso → JuP1 crossing requires import date and invoice.");
        if (!string.IsNullOrWhiteSpace(p.ImportInvoice))
        {
            if (!Identifiers.IsInvoice(p.ImportInvoice)) W("VAL-023", nameof(p.ImportInvoice), "Invoice does not match MPS########.");
            else if (p.ImportDate is not null && Identifiers.InvoiceYear(p.ImportInvoice) != p.ImportDate.Value.Year)
                W("VAL-023", nameof(p.ImportInvoice), $"Invoice year ({Identifiers.InvoiceYear(p.ImportInvoice)}) does not match the import date ({p.ImportDate:yyyy}).");
        }
        if (!string.IsNullOrWhiteSpace(p.ReturnPONumber) && !Identifiers.IsPurchaseOrder(p.ReturnPONumber))
            W("VAL-024", nameof(p.ReturnPONumber), "PO number must have 8 digits.");

        // ---- Etapa 3 ----
        var stage3Data = p.PhysicalDeliveredQty is not null || p.OldMaterialDeliveredQty is not null || (p.ScrapQty ?? 0) > 0 || p.NewHU is not null;
        if (stage3Data && p.ImportDateElpJup1 is null) E("VAL-030", nameof(p.PhysicalDeliveredQty), "MFO execution cannot be recorded without the El Paso → JuP1 crossing.");
        var reason = Catalogs.FindReason(p.ReasonId);
        if (reason is { RequiresNewMaterial: true })
        {
            if (stage3Data && string.IsNullOrWhiteSpace(p.NewMaterial)) E("VAL-031", nameof(p.NewMaterial), "Reflash: enter the new part number.");
            else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial == p.ReturnedMaterial) E("VAL-031", nameof(p.NewMaterial), "Reflash: the new part number must differ from the original.");
        }
        else if (!string.IsNullOrWhiteSpace(p.NewMaterial) && p.NewMaterial != p.ReturnedMaterial)
            W("VAL-031", nameof(p.NewMaterial), "New part number differs from the original but the reason is not 'reflash'.");
        if (!string.IsNullOrWhiteSpace(p.NewMaterial) && !Identifiers.IsValidMaterial(p.NewMaterial))
            E("VAL-010", nameof(p.NewMaterial), "New part number does not match XXXX.XXX.XXX-XX(X).");
        if (p.PhysicalDeliveredQty is not null && p.OldMaterialDeliveredQty is not null
            && p.PhysicalDeliveredQty != p.OldMaterialDeliveredQty + (p.ReinsertionQty ?? 0))
            E("VAL-032", nameof(p.PhysicalDeliveredQty), "Physical delivered to LOW must equal old material delivered + reinsertion quantity.");
        var outQty = (p.OldMaterialDeliveredQty ?? 0) + (p.ScrapQty ?? 0);
        if (outQty > p.QtyImported) E("VAL-033", nameof(p.OldMaterialDeliveredQty), $"Old material delivered + scrap ({outQty}) exceeds the quantity imported ({p.QtyImported}).");
        if (p.PhysicalDeliveredQty is not null && outQty < p.QtyImported)
            W("VAL-034", nameof(p.PhysicalDeliveredQty), $"{p.QtyImported - outQty} pcs not reconciled: they are counted as missing and the pallet goes to duty risk. QMM must classify it in Status category.");
        if ((p.ScrapQty ?? 0) > 0 && p.ScrapDate is null) E("VAL-035", nameof(p.ScrapDate), "A scrap quantity greater than 0 requires the scrap date.");
        if (p.ScrapDate < p.ImportDateElpJup1) E("VAL-035", nameof(p.ScrapDate), "Scrap date cannot be earlier than the crossing.");
        if (!string.IsNullOrWhiteSpace(p.NewHU) && !Identifiers.IsHandlingUnit(p.NewHU))
            W("VAL-036", nameof(p.NewHU), "New HU is not 10 digits. [VALIDATE rule]");
        if (p.PhysicalDeliveredQty < 0 || p.ReinsertionQty < 0 || p.OldMaterialDeliveredQty < 0 || p.ScrapQty < 0)
            E("VAL-038", nameof(p.PhysicalDeliveredQty), "Quantities cannot be negative.");
        if (p.DateSentBack < p.ImportDateElpJup1) E("VAL-037", nameof(p.DateSentBack), "Sent-back date cannot be earlier than the crossing.");

        // ---- Etapa 4 ----
        if ((p.LowExportNumber is not null || p.LowExportDate is not null) && (p.OldMaterialDeliveredQty ?? 0) == 0)
            E("VAL-040", nameof(p.LowExportNumber), "LOW export requires old material delivered to LOW.");
        if (p.LowExportNumber is <= 0 or > 9999) E("VAL-040", nameof(p.LowExportNumber), "LOW export number must be between 1 and 9999.");
        if (p.LowExportDate < p.DateSentBack) W("VAL-040", nameof(p.LowExportDate), "LOW export date is earlier than the sent-back date.");
        var hseAny = p.HseConfirmationDate is not null || p.HseScrapKg is not null || !string.IsNullOrWhiteSpace(p.ScrapGrNumber);
        if (hseAny)
        {
            if ((p.ScrapQty ?? 0) <= 0) E("VAL-041", nameof(p.ScrapQty), "HSE confirmed the destruction, but the scrap quantity is 0.");
            if (p.HseConfirmationDate < p.ScrapDate) E("VAL-043", nameof(p.HseConfirmationDate), "HSE confirmation cannot be earlier than the scrap date.");
            if (!string.IsNullOrWhiteSpace(p.ScrapGrNumber) && !Identifiers.IsGrNumber(p.ScrapGrNumber)) E("VAL-044", nameof(p.ScrapGrNumber), "GR number must be 10 digits.");
            if (p.HseConfirmationDate is not null && (string.IsNullOrWhiteSpace(p.ScrapGrNumber) || p.HseScrapKg is null or <= 0))
                E("VAL-045", nameof(p.HseConfirmationDate), "HSE confirmation requires GR number and kg greater than 0.");
        }
        switch (p.ClosureType)
        {
            case ClosureDocumentType.Unrecognized:
                E("VAL-046", nameof(p.CustomsExportReference), "Customs export must be PRyy-##### (or F4 / V1). Text such as 'scrap marzo' does not close the pallet.");
                break;
            case ClosureDocumentType.ExportPedimento when (p.OldMaterialDeliveredQty ?? 0) == 0:
                E("VAL-046", nameof(p.CustomsExportReference), "Customs export requires old material delivered to LOW.");
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
        yield return (nameof(p.LowExportDate), p.LowExportDate);
        yield return (nameof(p.HseConfirmationDate), p.HseConfirmationDate);
    }
}
