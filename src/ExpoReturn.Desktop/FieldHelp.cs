namespace ExpoReturn.Desktop;

/// <summary>
/// Ayuda contextual por campo (tooltip): qué es, la regla y un ejemplo.
/// El nombre visible del campo es el encabezado de la columna del Excel 2.0 (ver <see cref="FieldLabels"/>).
/// </summary>
public static class FieldHelp
{
    private sealed record Entry(string Rule, string? Example = null);

    private static readonly Dictionary<string, Entry> Map = new()
    {
        // ---- Pestaña 1 · Solicitud ----
        ["ValueStreamId"] = new("Hoja del Excel (VS1, VS4, VS5, VS7 o VS9). Sólo se elige al crear el retorno; define el consecutivo y quién puede editar."),
        ["FolioNumber"] = new("Consecutivo por hoja VS. Lo asigna el sistema al guardar por primera vez.", "VS7-00012"),
        ["QmmResponsible"] = new("Champion o Proxy QMM del VS según la matriz Champions VS. Obligatorio para pasar a Solicitado (VAL-017)."),
        ["RequestingDepartment"] = new("Departamento que pide el retorno. Obligatorio (VAL-016).", "MFQ4, LOP2, PUQ"),
        ["RequestorName"] = new("Persona que solicita. Obligatorio.", "Montaño, Sofía"),
        ["ReasonId"] = new("Motivo de la lista del Excel (10 motivos). Obligatorio (VAL-015). Si es reflash, se exigirá el New PN."),
        ["ReturnReasonDetail"] = new("Texto libre para explicar el caso.", "Contaminación en conector, sorteo 100 %"),
        ["ReturnedMaterial"] = new("Número de parte en formato SAP XXXX.XXX.XXX-XX(X) (VAL-010). Puede capturarse sin puntos: se normaliza solo. Obligatorio.", "0265957012-24W → 0265.957.012-24W"),
        ["UnitCostUsd"] = new("Costo unitario de la hoja MM60 COST SAP (Price / Price unit). Sin costo aparece \"Sin costo (COSTEAR)\" (VAL-011). Calculado."),
        ["SapUsdTotal"] = new("Quantity imported × MM60 SAP UNIT COST. Calculado."),
        ["QtyImported"] = new("Piezas importadas temporalmente. Entero mayor a 0 (VAL-012). Obligatorio.", "528"),
        ["OriginalHU"] = new("HU de 10 dígitos (VAL-013). Capture el HU o el Original Batch.", "2107104180"),
        ["OriginalBatch"] = new("Lote SAP, dos letras y ocho dígitos (VAL-014).", "AA23743599"),
        ["DateReturnedToWhs"] = new("Fecha de la PO (en VS4 la columna se llama \"Date Returned to WHS\"). La captura QMM. [VALIDAR significado]"),
        ["ReturnPONumber"] = new("PO de retorno de 8 dígitos; puede incluir la posición como en el Excel (VAL-024). La captura QMM.", "49398082 - 999"),
        // ---- Pestaña 2 · Aduana / entrada ----
        ["ImportDate"] = new("Fecha de importación. No anterior a Date PO (VAL-021). La captura GS/OSD43.", "30/10/2026"),
        ["ImportInvoice"] = new("Factura de importación MPS + 8 dígitos; su año debe coincidir con Import Date (VAL-023).", "MPS26000222"),
        ["ImportDateElpJup1"] = new("Fecha de cruce El Paso → JuP1. Abre el ciclo IMMEX (inicia antigüedad y plazo legal). Requiere Import Date e Invoice (VAL-022)."),
        ["DeliveryNumber"] = new("Número de entrega que registra LOP."),
        // ---- Pestaña 3 · Ejecución MFO ----
        ["NewMaterial"] = new("Número de parte después del retrabajo. Obligatorio y distinto al original si Reason es reflash (VAL-031).", "0261.S10.8GT-6DV"),
        ["PhysicalDeliveredQty"] = new("= Old Material Delivered to LOW + New Reinsertion Material Qty (VAL-032). Al capturarlo se CIERRA el retrabajo MFO; si faltan piezas quedan como faltante (VAL-034)."),
        ["NewHU"] = new("HU nuevo de 10 dígitos (VAL-036).", "2106697268"),
        ["ReworkBatch"] = new("Lote del pallet retrabajado (segunda columna \"Original Batch\" del Excel)."),
        ["ReinsertionQty"] = new("Material NUEVO agregado al pallet. No es importación temporal."),
        ["DateSentBack"] = new("Fecha de regreso del material. No anterior al cruce (VAL-037)."),
        ["OldMaterialDeliveredQty"] = new("Piezas IMPORTADAS que regresan a LOW. Old Material + Scrap no puede exceder lo importado (VAL-033).", "527"),
        ["ScrapQty"] = new("Piezas importadas que se destruyen. Si es mayor a 0 requiere Scrap Date (VAL-035)."),
        ["ScrapDate"] = new("Fecha de envío a scrap. No anterior al cruce."),
        // ---- Pestaña 4 · Cierre salida / HSE ----
        ["LowExport"] = new("Texto de LOW sobre la expo, como en el Excel.", "Incluido en expo #4 de el dia 17/07/2026"),
        ["CustomsExportReference"] = new("PRyy-##### (exportación), F4 (cambio de régimen) o V1 (pedimento virtual). Textos o fórmulas NO cierran (VAL-046).", "PR26-00101"),
        ["ScrapGrNumber"] = new("Número de GR del scrap, 10 dígitos (VAL-044). Lo captura MFO.", "4951422891"),
        ["HseScrapKg"] = new("Kilos destruidos, mayor a 0 (VAL-045).", "0.5"),
        ["HseConfirmationDate"] = new("Con GR Number + kg + fecha, el scrap sale del balance. No anterior a Scrap Date (VAL-043)."),
        // ---- Pestaña 5 · Balance ----
        ["StatusCategoryCode"] = new("Lista de COMMENTS de la hoja VS del Excel (cada hoja tiene su propia lista). EXPORT, SCRAP e INVENTORY se asignan solas; las discrepancias las asigna QMM."),
        ["MfoInventoryUsd"] = new("Piezas aún en piso MFO × costo."),
        ["HseScrapUsd"] = new("Scrap sin confirmación HSE × costo."),
        ["MfoLowFailureUsd"] = new("(En LOW sin pedimento + faltantes) × costo."),
        ["BalanceGeneralUsd"] = new("Suma de MFO INVENTORY + HSE SCRAP + MFO-LOW FAILURE: valor aún no conciliado."),
        ["DutiesToPayUsd"] = new("MFO-LOW FAILURE USD × 0.19 (tasa provisional). Bloque \"AFTER JUNE 2025\" del Excel."),
    };

    public static string Get(string field)
    {
        if (!Map.TryGetValue(field, out var e)) return FieldLabels.Get(field);
        return e.Example is null ? e.Rule : $"{e.Rule}{Environment.NewLine}Ejemplo: {e.Example}";
    }
}

/// <summary>Indexador para ToolTip="{Binding Help[Campo]}".</summary>
public sealed class HelpMap
{
    public static HelpMap Instance { get; } = new();
    public string this[string field] => FieldHelp.Get(field);
}

/// <summary>Nombre de cada campo = encabezado de su columna en el Excel 2.0. Se usa en el formulario, mensajes y bitácora.</summary>
public static class FieldLabels
{
    /// <summary>Obligatorios para pasar a "Solicitado" (UX-B04).</summary>
    public static readonly HashSet<string> Required = new()
    {
        "QmmResponsible", "RequestingDepartment", "RequestorName", "ReasonId", "ReturnedMaterial", "QtyImported", "OriginalHU", "OriginalBatch",
    };

    private static readonly Dictionary<string, string> Map = new()
    {
        ["ValueStreamId"] = "VS", ["FolioNumber"] = "#",
        ["QmmResponsible"] = "QMM Responsible", ["RequestingDepartment"] = "Requesting department", ["RequestorName"] = "Requestor",
        ["ReasonId"] = "Reason", ["ReturnReasonDetail"] = "Return Reason", ["ReturnedMaterial"] = "Returned Pallet 13 Digits Part number",
        ["UnitCostUsd"] = "MM60 SAP UNIT COST", ["SapUsdTotal"] = "SAP USD TOTAL", ["QtyImported"] = "Quantity imported",
        ["OriginalHU"] = "Original #HU", ["OriginalBatch"] = "Original Batch", ["DateReturnedToWhs"] = "Date PO", ["ReturnPONumber"] = "PO Number",
        ["ImportDate"] = "Import Date", ["ImportInvoice"] = "Invoice", ["ImportDateElpJup1"] = "Import date ELP-JuP1",
        ["DeliveryNumber"] = "Delivery Number",
        ["NewMaterial"] = "Returned Pallet 13 Dgt New PN", ["PhysicalDeliveredQty"] = "Physical material delivered to LOW", ["NewHU"] = "New HU",
        ["ReworkBatch"] = "Original Batch", ["ReinsertionQty"] = "New Reinsertion Material Qty", ["DateSentBack"] = "Date send Back",
        ["OldMaterialDeliveredQty"] = "Old Material Delivered to LOW", ["ScrapQty"] = "Scrap to HSE (Qty)", ["ScrapDate"] = "Scrap Date",
        ["LowExport"] = "LOW export", ["CustomsExportReference"] = "Customs Export", ["ScrapGrNumber"] = "GR Number",
        ["HseScrapKg"] = "Kg Qty from HSE", ["HseConfirmationDate"] = "HSE confirmation Date",
        ["BalanceGeneralUsd"] = "Balance GENERAL DLLS", ["MfoInventoryUsd"] = "MFO INVENTORY USD", ["HseScrapUsd"] = "HSE SCRAP USD",
        ["MfoLowFailureUsd"] = "MFO-LOW FAILURE USD", ["DutiesToPayUsd"] = "DUTIES TO PAY USD",
        ["StatusCategoryCode"] = "COMMENTS", ["Status"] = "Estatus", ["Registro"] = "Registro",
    };

    public static string Get(string field) => Map.TryGetValue(field, out var l) ? l : field;
    public static string Label(string field) => Required.Contains(field) ? $"{Get(field)} *" : Get(field);
}

/// <summary>Muestra el nombre del campo (encabezado del Excel) en tablas como la bitácora.</summary>
public sealed class FieldLabelConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is string f ? FieldLabels.Get(f) : value;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
