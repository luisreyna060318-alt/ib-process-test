namespace ExpoReturn.Desktop;

/// <summary>
/// Field help (tooltip): Excel 2.0 column, what it is, the rule and an example.
/// Names and Excel columns are also in entregables/Diccionario_Campos_ExpoReturn.xlsx.
/// </summary>
public static class FieldHelp
{
    private sealed record Entry(string Rule, string? Example = null);

    private static readonly Dictionary<string, Entry> Map = new()
    {
        ["ValueStreamId"] = new("Chosen only when the return is created. Defines the folio sequence and who can edit."),
        ["FolioNumber"] = new("Sequential per Value Stream, assigned by the system on first save.", "VS7-00012"),
        ["QmmResponsible"] = new("QMM Champion or Proxy of the VS (Users & Access). Required to move to Requested (VAL-017)."),
        ["RequestingDepartment"] = new("Department that requests the return. Required (VAL-016).", "MFQ4, LOP2, PUQ"),
        ["RequestorName"] = new("Person who requests. Required.", "Montaño, Sofía"),
        ["ReasonId"] = new("One of the 10 reasons of the Excel list. Required (VAL-015). If it is reflash, the new part number is required."),
        ["ReturnReasonDetail"] = new("Free text explaining the case.", "Connector contamination, 100% sorting"),
        ["ReturnedMaterial"] = new("SAP format XXXX.XXX.XXX-XX(X) (VAL-010). It can be typed without dots: it is normalized. Required.", "0265957012-24W → 0265.957.012-24W"),
        ["UnitCostUsd"] = new("Taken from the MM60 cost master (Price / Price unit) that the Owner loads with Import MM60 file. Without cost it shows \"No cost (COSTEAR)\" (VAL-011). Calculated."),
        ["SapUsdTotal"] = new("Quantity imported × unit cost. Calculated."),
        ["QtyImported"] = new("Pieces temporarily imported. Whole number greater than 0 (VAL-012). Required.", "528"),
        ["OriginalHU"] = new("10-digit handling unit (VAL-013). Enter the HU or the batch.", "2107104180"),
        ["OriginalBatch"] = new("SAP batch: two letters and eight digits (VAL-014).", "AA23743599"),
        ["DateReturnedToWhs"] = new("PO date / date returned to the El Paso warehouse. Entered by QMM. [VALIDATE meaning]"),
        ["ReturnPONumber"] = new("8-digit return PO number (VAL-024). Entered by QMM.", "49398082"),
        ["ImportDate"] = new("Import date. Not earlier than the PO date (VAL-021). Entered by GS/OSD43.", "30/10/2026"),
        ["ImportInvoice"] = new("Import invoice MPS + 8 digits; its year must match the import date (VAL-023).", "MPS26000222"),
        ["ImportDateElpJup1"] = new("El Paso → JuP1 crossing date. Opens the IMMEX cycle (aging and legal term). Requires import date and invoice (VAL-022)."),
        ["DeliveryNumber"] = new("Delivery number recorded by LOP."),
        ["NewMaterial"] = new("Part number after rework. Required and different from the original if the reason is reflash (VAL-031).", "0261.S10.8GT-6DV"),
        ["PhysicalDeliveredQty"] = new("Old material delivered + reinsertion (VAL-032). Entering it CLOSES the MFO rework; missing pieces count as missing (VAL-034)."),
        ["NewHU"] = new("10-digit new HU (VAL-036).", "2106697268"),
        ["ReworkBatch"] = new("Batch of the reworked pallet."),
        ["ReinsertionQty"] = new("NEW material added to the pallet. It is not a temporary import."),
        ["DateSentBack"] = new("Date the material was sent back. Not earlier than the crossing (VAL-037)."),
        ["OldMaterialDeliveredQty"] = new("IMPORTED pieces returned to LOW. Old material + scrap cannot exceed the quantity imported (VAL-033).", "527"),
        ["ScrapQty"] = new("Imported pieces destroyed. Greater than 0 requires the scrap date (VAL-035)."),
        ["ScrapDate"] = new("Date sent to scrap. Not earlier than the crossing."),
        ["LowExportNumber"] = new("LOW expo number (the Excel column held \"Incluido en expo #4 de el dia …\").", "4"),
        ["LowExportDate"] = new("Date of the LOW expo (same Excel column as the number).", "17/07/2026"),
        ["CustomsExportReference"] = new("PRyy-##### (export), F4 (regime change) or V1 (virtual pedimento). Text or formulas do NOT close (VAL-046).", "PR26-00101"),
        ["ScrapGrNumber"] = new("Scrap GR number, 10 digits (VAL-044). Entered by MFO.", "4951422891"),
        ["HseScrapKg"] = new("Kilograms destroyed, greater than 0 (VAL-045).", "0.5"),
        ["HseConfirmationDate"] = new("With GR + kg + date, the scrap leaves the balance. Not earlier than the scrap date (VAL-043)."),
        ["StatusCategoryCode"] = new("Options of the COMMENTS list of the VS sheet (each sheet has its own). EXPORT, SCRAP and INVENTORY are set automatically; discrepancies are set by QMM."),
        ["MfoInventoryUsd"] = new("Pieces still on the MFO floor × cost."),
        ["HseScrapUsd"] = new("Scrap without HSE confirmation × cost."),
        ["MfoLowFailureUsd"] = new("(In LOW without pedimento + missing) × cost."),
        ["BalanceGeneralUsd"] = new("Sum of the three indicators above: value not reconciled yet."),
        ["DutiesToPayUsd"] = new("MFO-LOW failure × 0.19 (provisional rate)."),
    };

    public static string Get(string field)
    {
        var parts = new List<string>();
        if (FieldLabels.ExcelHeader(field) is { } excel) parts.Add($"Excel 2.0: {excel}");
        if (Map.TryGetValue(field, out var e))
        {
            parts.Add(e.Rule);
            if (e.Example is not null) parts.Add($"Example: {e.Example}");
        }
        return parts.Count == 0 ? FieldLabels.Get(field) : string.Join(Environment.NewLine, parts);
    }
}

/// <summary>Indexer for ToolTip="{Binding Help[Field]}".</summary>
public sealed class HelpMap
{
    public static HelpMap Instance { get; } = new();
    public string this[string field] => FieldHelp.Get(field);
}

/// <summary>Field name (form, tables, messages, audit log) and its Excel 2.0 column header.</summary>
public static class FieldLabels
{
    /// <summary>Required to move to "Requested" (UX-B04).</summary>
    public static readonly HashSet<string> Required = new()
    {
        "QmmResponsible", "RequestingDepartment", "RequestorName", "ReasonId", "ReturnedMaterial", "QtyImported", "OriginalHU", "OriginalBatch",
    };

    // field → (name in the app, Excel 2.0 header)
    private static readonly Dictionary<string, (string Label, string? Excel)> Map = new()
    {
        ["ValueStreamId"] = ("Value Stream (VS)", "VS1 · VS4 · VS5 · VS7 · VS9 sheet"),
        ["FolioNumber"] = ("Folio", "#"),
        ["QmmResponsible"] = ("QMM Responsible", "QMM Responsible"),
        ["RequestingDepartment"] = ("Requesting Department", "Requesting department"),
        ["RequestorName"] = ("Requestor", "Requestor"),
        ["ReasonId"] = ("Reason", "Reason"),
        ["ReturnReasonDetail"] = ("Reason Detail", "\"Return Reason\" 22.09.26"),
        ["ReturnedMaterial"] = ("Returned Part Number", "Returned Pallet 13 Digits Part number"),
        ["UnitCostUsd"] = ("MM60 Unit Cost (USD)", "MM60 SAP UNIT COST"),
        ["SapUsdTotal"] = ("SAP Total (USD)", "SAP USD TOTAL"),
        ["QtyImported"] = ("Quantity Imported", "Quantity imported"),
        ["OriginalHU"] = ("Original HU", "Original #HU"),
        ["OriginalBatch"] = ("Original Batch", "Original Batch"),
        ["DateReturnedToWhs"] = ("PO Date / Returned to WHS", "Date PO"),
        ["ReturnPONumber"] = ("PO Number", "PO Number"),
        ["ImportDate"] = ("Import Date", "Import Date"),
        ["ImportInvoice"] = ("Import Invoice", "Invoice"),
        ["ImportDateElpJup1"] = ("ELP → JuP1 Crossing Date", "Import date ELP-JuP1"),
        ["DeliveryNumber"] = ("Delivery Number", "Delivery Number 22.09.26"),
        ["NewMaterial"] = ("New Part Number", "Returned Pallet 13 Dgt New PN"),
        ["PhysicalDeliveredQty"] = ("Physical Delivered to LOW", "Physical material delivered to LOW"),
        ["NewHU"] = ("New HU", "New HU"),
        ["ReworkBatch"] = ("Rework Batch", "Original Batch (second column)"),
        ["ReinsertionQty"] = ("Reinsertion Qty", "New Reinsertion Material Qty"),
        ["DateSentBack"] = ("Sent Back Date", "Date send Back"),
        ["OldMaterialDeliveredQty"] = ("Old Material Delivered to LOW", "Old Material Delivered to LOW"),
        ["ScrapQty"] = ("Scrap Qty (HSE)", "Scrap to HSE (Qty)"),
        ["ScrapDate"] = ("Scrap Date", "Scrap Date"),
        ["LowExportNumber"] = ("LOW Export Number", "LOW export (expo number)"),
        ["LowExportDate"] = ("LOW Export Date", "LOW export (date)"),
        ["CustomsExportReference"] = ("Customs Export", "Customs Export"),
        ["ScrapGrNumber"] = ("GR Number", "GR Number"),
        ["HseScrapKg"] = ("HSE Scrap (kg)", "Kg Qty from HSE"),
        ["HseConfirmationDate"] = ("HSE Confirmation Date", "HSE confirmation Date"),
        ["StatusCategoryCode"] = ("Status Category", "COMMENTS"),
        ["BalanceGeneralUsd"] = ("General Balance (USD)", "Balance GENERAL DLLS"),
        ["MfoInventoryUsd"] = ("MFO Inventory (USD)", "MFO INVENTORY USD"),
        ["HseScrapUsd"] = ("HSE Scrap (USD)", "HSE SCRAP USD"),
        ["MfoLowFailureUsd"] = ("MFO-LOW Failure (USD)", "MFO-LOW FAILURE USD"),
        ["DutiesToPayUsd"] = ("Duties to Pay (USD)", "DUTIES TO PAY USD"),
        ["Status"] = ("Status", null),
        ["Record"] = ("Record", null),
    };

    public static string Get(string field) => Map.TryGetValue(field, out var e) ? e.Label : field;
    public static string? ExcelHeader(string field) => Map.TryGetValue(field, out var e) ? e.Excel : null;
    public static string Label(string field) => Required.Contains(field) ? $"{Get(field)} *" : Get(field);
}

/// <summary>Shows the field name in tables such as the audit log.</summary>
public sealed class FieldLabelConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is string f ? FieldLabels.Get(f) : value;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
