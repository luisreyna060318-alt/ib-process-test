namespace ExpoReturn.Desktop;

/// <summary>
/// Ayuda contextual por campo (tooltip): encabezado de la columna en el Excel 2.0, qué es, la regla y un ejemplo.
/// El nombre visible es la etiqueta limpia de <see cref="FieldLabels"/>; el diccionario completo está en
/// entregables/Diccionario_Campos_ExpoReturn.xlsx.
/// </summary>
public static class FieldHelp
{
    private sealed record Entry(string Rule, string? Example = null);

    private static readonly Dictionary<string, Entry> Map = new()
    {
        // ---- Pestaña 1 · Solicitud ----
        ["ValueStreamId"] = new("Sólo se elige al crear el retorno. Define el consecutivo del folio y quién puede editar."),
        ["FolioNumber"] = new("Consecutivo por hoja VS. Lo asigna el sistema al guardar por primera vez.", "VS7-00012"),
        ["QmmResponsible"] = new("Champion o Proxy QMM del VS según la matriz Champions VS. Obligatorio para pasar a Solicitado (VAL-017)."),
        ["RequestingDepartment"] = new("Departamento que pide el retorno. Obligatorio (VAL-016).", "MFQ4, LOP2, PUQ"),
        ["RequestorName"] = new("Persona que solicita. Obligatorio.", "Montaño, Sofía"),
        ["ReasonId"] = new("Motivo de la lista del Excel (10 motivos). Obligatorio (VAL-015). Si es reflash, se exigirá el número de parte nuevo."),
        ["ReturnReasonDetail"] = new("Texto libre para explicar el caso.", "Contaminación en conector, sorteo 100 %"),
        ["ReturnedMaterial"] = new("Número de parte en formato SAP XXXX.XXX.XXX-XX(X) (VAL-010). Puede capturarse sin puntos: se normaliza solo. Obligatorio.", "0265957012-24W → 0265.957.012-24W"),
        ["UnitCostUsd"] = new("Se toma del maestro de costos MM60 (Price / Price unit). Sin costo aparece \"Sin costo (COSTEAR)\" (VAL-011). Calculado."),
        ["SapUsdTotal"] = new("Cantidad importada × costo unitario. Calculado."),
        ["QtyImported"] = new("Piezas importadas temporalmente. Entero mayor a 0 (VAL-012). Obligatorio.", "528"),
        ["OriginalHU"] = new("HU de 10 dígitos (VAL-013). Capture el HU o el lote.", "2107104180"),
        ["OriginalBatch"] = new("Lote SAP, dos letras y ocho dígitos (VAL-014).", "AA23743599"),
        ["DateReturnedToWhs"] = new("Fecha de la PO / retorno al almacén de El Paso. La captura QMM. [VALIDAR significado]"),
        ["ReturnPONumber"] = new("PO de retorno de 8 dígitos; puede incluir la posición como en el Excel (VAL-024). La captura QMM.", "49398082 - 999"),
        // ---- Pestaña 2 · Aduana / entrada ----
        ["ImportDate"] = new("Fecha de importación. No anterior a la fecha PO (VAL-021). La captura GS/OSD43.", "30/10/2026"),
        ["ImportInvoice"] = new("Factura de importación MPS + 8 dígitos; su año debe coincidir con la fecha de importación (VAL-023).", "MPS26000222"),
        ["ImportDateElpJup1"] = new("Fecha de cruce El Paso → JuP1. Abre el ciclo IMMEX (inicia antigüedad y plazo legal). Requiere fecha de importación y factura (VAL-022)."),
        ["DeliveryNumber"] = new("Número de entrega que registra LOP."),
        // ---- Pestaña 3 · Ejecución MFO ----
        ["NewMaterial"] = new("Número de parte después del retrabajo. Obligatorio y distinto al original si el motivo es reflash (VAL-031).", "0261.S10.8GT-6DV"),
        ["PhysicalDeliveredQty"] = new("= material original devuelto + reinsertado (VAL-032). Al capturarlo se CIERRA el retrabajo MFO; si faltan piezas quedan como faltante (VAL-034)."),
        ["NewHU"] = new("HU nuevo de 10 dígitos (VAL-036).", "2106697268"),
        ["ReworkBatch"] = new("Lote del pallet retrabajado."),
        ["ReinsertionQty"] = new("Material NUEVO agregado al pallet. No es importación temporal."),
        ["DateSentBack"] = new("Fecha de regreso del material. No anterior al cruce (VAL-037)."),
        ["OldMaterialDeliveredQty"] = new("Piezas IMPORTADAS que regresan a LOW. Devuelto + scrap no puede exceder lo importado (VAL-033).", "527"),
        ["ScrapQty"] = new("Piezas importadas que se destruyen. Si es mayor a 0 requiere fecha de scrap (VAL-035)."),
        ["ScrapDate"] = new("Fecha de envío a scrap. No anterior al cruce."),
        // ---- Pestaña 4 · Cierre salida / HSE ----
        ["LowExport"] = new("Texto de LOW sobre la expo (número y fecha).", "Incluido en expo #4 de el dia 17/07/2026"),
        ["CustomsExportReference"] = new("PRyy-##### (exportación), F4 (cambio de régimen) o V1 (pedimento virtual). Textos o fórmulas NO cierran (VAL-046).", "PR26-00101"),
        ["ScrapGrNumber"] = new("Número de GR del scrap, 10 dígitos (VAL-044). Lo captura MFO.", "4951422891"),
        ["HseScrapKg"] = new("Kilos destruidos, mayor a 0 (VAL-045).", "0.5"),
        ["HseConfirmationDate"] = new("Con GR + kg + fecha, el scrap sale del balance. No anterior a la fecha de scrap (VAL-043)."),
        // ---- Pestaña 5 · Balance ----
        ["StatusCategoryCode"] = new("Opciones de la lista COMMENTS de la hoja VS del Excel (cada hoja tiene la suya). EXPORT, SCRAP e INVENTORY se asignan solas; las discrepancias las asigna QMM."),
        ["MfoInventoryUsd"] = new("Piezas aún en piso MFO × costo."),
        ["HseScrapUsd"] = new("Scrap sin confirmación HSE × costo."),
        ["MfoLowFailureUsd"] = new("(En LOW sin pedimento + faltantes) × costo."),
        ["BalanceGeneralUsd"] = new("Suma de los tres indicadores anteriores: valor aún no conciliado."),
        ["DutiesToPayUsd"] = new("Falla MFO-LOW × 0.19 (tasa provisional)."),
    };

    public static string Get(string field)
    {
        var parts = new List<string>();
        if (FieldLabels.ExcelHeader(field) is { } excel) parts.Add($"Excel 2.0: {excel}");
        if (Map.TryGetValue(field, out var e))
        {
            parts.Add(e.Rule);
            if (e.Example is not null) parts.Add($"Ejemplo: {e.Example}");
        }
        return parts.Count == 0 ? FieldLabels.Get(field) : string.Join(Environment.NewLine, parts);
    }
}

/// <summary>Indexador para ToolTip="{Binding Help[Campo]}".</summary>
public sealed class HelpMap
{
    public static HelpMap Instance { get; } = new();
    public string this[string field] => FieldHelp.Get(field);
}

/// <summary>
/// Nombre limpio de cada campo (formulario, tablas, mensajes y bitácora) y el encabezado de su columna en el Excel 2.0.
/// Los mismos pares están en el diccionario entregables/Diccionario_Campos_ExpoReturn.xlsx.
/// </summary>
public static class FieldLabels
{
    /// <summary>Obligatorios para pasar a "Solicitado" (UX-B04).</summary>
    public static readonly HashSet<string> Required = new()
    {
        "QmmResponsible", "RequestingDepartment", "RequestorName", "ReasonId", "ReturnedMaterial", "QtyImported", "OriginalHU", "OriginalBatch",
    };

    // campo → (nombre limpio, encabezado en el Excel 2.0)
    private static readonly Dictionary<string, (string Label, string? Excel)> Map = new()
    {
        ["ValueStreamId"] = ("Value Stream (VS)", "Hoja VS1 · VS4 · VS5 · VS7 · VS9"),
        ["FolioNumber"] = ("Folio", "#"),
        ["QmmResponsible"] = ("Responsable QMM (Champion / Proxy)", "QMM Responsible"),
        ["RequestingDepartment"] = ("Departamento solicitante", "Requesting department"),
        ["RequestorName"] = ("Solicitante (Apellido, Nombre)", "Requestor"),
        ["ReasonId"] = ("Motivo oficial", "Reason"),
        ["ReturnReasonDetail"] = ("Detalle del motivo", "\"Return Reason\" 22.09.26"),
        ["ReturnedMaterial"] = ("Número de parte devuelto (13 dígitos)", "Returned Pallet 13 Digits Part number"),
        ["UnitCostUsd"] = ("Costo unitario MM60 (USD)", "MM60 SAP UNIT COST"),
        ["SapUsdTotal"] = ("Valor total SAP (USD)", "SAP USD TOTAL"),
        ["QtyImported"] = ("Cantidad importada", "Quantity / Quantity imported"),
        ["OriginalHU"] = ("HU original (10 dígitos)", "Original #HU"),
        ["OriginalBatch"] = ("Lote original", "Original Batch"),
        ["DateReturnedToWhs"] = ("Fecha PO / retorno a almacén", "Date PO / Date Returned to WHS"),
        ["ReturnPONumber"] = ("PO de retorno", "PO Number / Return PO"),
        ["ImportDate"] = ("Fecha de importación", "Import Date"),
        ["ImportInvoice"] = ("Factura de importación", "Invoice"),
        ["ImportDateElpJup1"] = ("Fecha de cruce El Paso → JuP1", "Import date ELP-JuP1"),
        ["DeliveryNumber"] = ("Número de entrega (LOP)", "Delivery Number 22.09.26"),
        ["NewMaterial"] = ("Número de parte nuevo", "Returned Pallet 13 Dgt New PN"),
        ["PhysicalDeliveredQty"] = ("Entregado físicamente a LOW", "Physical material delivered to LOW"),
        ["NewHU"] = ("HU nuevo", "New HU"),
        ["ReworkBatch"] = ("Lote del retrabajo", "Original Batch (segunda columna)"),
        ["ReinsertionQty"] = ("Material reinsertado (cantidad)", "New Reinsertion Material Qty"),
        ["DateSentBack"] = ("Fecha de regreso", "Date send Back"),
        ["OldMaterialDeliveredQty"] = ("Material original devuelto a LOW", "Old Material Delivered to LOW"),
        ["ScrapQty"] = ("Cantidad a scrap (HSE)", "Scrap to HSE (Qty)"),
        ["ScrapDate"] = ("Fecha de scrap", "Scrap Date"),
        ["LowExport"] = ("Expo LOW", "LOW export"),
        ["CustomsExportReference"] = ("Exportación aduanal (PRyy-##### · F4 · V1)", "Customs Export"),
        ["ScrapGrNumber"] = ("Número de GR (MFO)", "GR Number"),
        ["HseScrapKg"] = ("Kg destruidos (HSE)", "Qty (Kg). from HSE / Kg Qty from HSE"),
        ["HseConfirmationDate"] = ("Fecha de confirmación HSE", "HSE confirmation Date"),
        ["StatusCategoryCode"] = ("Categoría de estatus", "COMMENTS / Date - Name: / Column2"),
        ["BalanceGeneralUsd"] = ("Balance general (USD)", "Balance GENERAL DLLS"),
        ["MfoInventoryUsd"] = ("Inventario MFO (USD)", "MFO INVENTORY USD"),
        ["HseScrapUsd"] = ("Scrap HSE sin confirmar (USD)", "HSE SCRAP USD"),
        ["MfoLowFailureUsd"] = ("Falla MFO-LOW (USD)", "MFO-LOW FAILURE USD"),
        ["DutiesToPayUsd"] = ("Aranceles por pagar (USD)", "DUTIES TO PAY USD (bloque AFTER JUNE 2025)"),
        ["Status"] = ("Estatus", null),
        ["Registro"] = ("Registro", null),
    };

    public static string Get(string field) => Map.TryGetValue(field, out var e) ? e.Label : field;
    public static string? ExcelHeader(string field) => Map.TryGetValue(field, out var e) ? e.Excel : null;
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
