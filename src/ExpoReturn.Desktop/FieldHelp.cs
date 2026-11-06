namespace ExpoReturn.Desktop;

/// <summary>
/// Ayuda contextual por campo (UX-B03 / UX-B05): nombre de la columna en el Excel 2.0, regla y ejemplo.
/// Las etiquetas visibles están en español; el nombre del Excel aparece como ayuda.
/// </summary>
public static class FieldHelp
{
    private sealed record Entry(string? Excel, string Rule, string? Example = null);

    private static readonly Dictionary<string, Entry> Map = new()
    {
        // ---- Etapa 1 · Solicitud ----
        ["ValueStreamId"] = new("Hoja VS1 · VS4 · VS5 · VS7 · VS9", "Sólo se elige al crear el retorno. Define el consecutivo del folio y quién puede editar."),
        ["FolioNumber"] = new("#", "Consecutivo por Value Stream. Lo asigna el sistema al guardar por primera vez.", "VS7-00012"),
        ["QmmResponsible"] = new("QMM Responsible", "Champion o Proxy QMM vigente del Value Stream (matriz Champions VS). Obligatorio para pasar a Solicitado (VAL-017)."),
        ["RequestingDepartment"] = new("Requesting department", "Departamento que pide el retorno. Obligatorio (VAL-016).", "MFQ4, LOP2, PUQ"),
        ["RequestorName"] = new("Requestor", "Persona que solicita, formato Apellido, Nombre. Obligatorio.", "Montaño, Sofía"),
        ["ReasonId"] = new("Reason", "Motivo oficial del catálogo (10 motivos). Obligatorio (VAL-015). Si es reflash, se exigirá el número de parte nuevo."),
        ["ReturnReasonDetail"] = new("\"Return Reason\" 22.09.26", "Texto libre para explicar el caso.", "Contaminación en conector, sorteo 100 %"),
        ["ReturnedMaterial"] = new("Returned Pallet 13 Digits Part number", "Formato SAP XXXX.XXX.XXX-XX(X) (VAL-010). Puede capturarse sin puntos: se normaliza solo. Obligatorio.", "0265957012-24W → 0265.957.012-24W"),
        ["UnitCostUsd"] = new("MM60 SAP UNIT COST", "Se toma del maestro de costos MM60 (Price / Price unit). Sin costo aparece \"Sin costo (COSTEAR)\" (VAL-011)."),
        ["SapUsdTotal"] = new("SAP USD TOTAL", "Cantidad importada × costo unitario. Calculado."),
        ["QtyImported"] = new("Quantity / Quantity imported", "Piezas importadas temporalmente. Entero mayor a 0 (VAL-012). Obligatorio.", "528"),
        ["OriginalHU"] = new("Original #HU", "Unidad de manipulación de 10 dígitos (VAL-013). Capture el HU o el lote.", "2107104180"),
        ["OriginalBatch"] = new("Original Batch", "Lote SAP, dos letras y ocho dígitos (VAL-014).", "AA23743599"),
        // ---- Etapa 2 · Aduana / entrada ----
        ["DateReturnedToWhs"] = new("Date PO / Date Returned to WHS", "Fecha de la PO / retorno al almacén de El Paso. [VALIDAR significado]"),
        ["ImportDate"] = new("Import Date", "Fecha del pedimento de importación. No anterior a la fecha PO (VAL-021).", "30/10/2026"),
        ["ImportInvoice"] = new("Invoice", "Factura de importación MPS + 8 dígitos; su año debe coincidir con la fecha de importación (VAL-023).", "MPS26000222"),
        ["ImportDateElpJup1"] = new("Import date ELP-JuP1", "Fecha de cruce El Paso → JuP1. Abre el ciclo IMMEX (inicia antigüedad y plazo legal). Requiere fecha de importación y factura (VAL-022)."),
        ["ImportPedimento"] = new(null, "Número de pedimento de importación (dato nuevo).", "26 07 3807 6000123"),
        ["DutyPaymentStatus"] = new(null, "Estatus del pago de aranceles: No requerido · Pendiente · Pagado (dato nuevo)."),
        ["ReturnPONumber"] = new("PO Number / Return PO", "PO de retorno de 8 dígitos; la posición va en su propio campo (VAL-024).", "49845363"),
        ["ReturnPOItem"] = new("PO Number (posición)", "Posición de la PO de retorno.", "999"),
        ["DeliveryNumber"] = new("Delivery Number 22.09.26", "Número de entrega que registra LOP."),
        // ---- Etapa 3 · Ejecución MFO / LOW ----
        ["IbOrderTypeCode"] = new(null, "Tipo de orden de reparación en planta (PPI2…PPI6)."),
        ["IbOrderNumber"] = new(null, "Número de orden IB en SAP.", "5100012"),
        ["NewMaterial"] = new("Returned Pallet 13 Dgt New PN", "Número de parte después del retrabajo. Obligatorio y distinto al original si el motivo es reflash (VAL-031).", "0261.S10.8GT-6DV"),
        ["NewHU"] = new("New HU", "HU nuevo de 10 dígitos (VAL-036).", "2106697268"),
        ["ReworkBatch"] = new("Original Batch (rework)", "Lote del pallet retrabajado."),
        ["ReinsertionQty"] = new("New Reinsertion Material Qty", "Material NUEVO agregado al pallet. No es importación temporal."),
        ["OldMaterialDeliveredQty"] = new("Old Material Delivered to LOW", "Piezas IMPORTADAS que regresan a LOW. Devuelto + scrap no puede exceder lo importado (VAL-033).", "527"),
        ["ScrapQty"] = new("Scrap to HSE (Qty)", "Piezas importadas que se destruyen. Si es mayor a 0 requiere fecha de scrap (VAL-035)."),
        ["ScrapDate"] = new("Scrap Date", "Fecha de envío a scrap. No anterior al cruce."),
        ["PhysicalDeliveredQty"] = new("Physical material delivered to LOW", "= material original devuelto + reinsertado (VAL-032). Al capturarlo se CIERRA el retrabajo MFO; si faltan piezas, declare el faltante (VAL-034)."),
        ["PhysicalDeliveredDate"] = new(null, "Fecha de entrega física a LOW."),
        ["DateSentBack"] = new("Date send Back", "Fecha de regreso del material. No anterior al cruce (VAL-037)."),
        ["DeclaredMissing"] = new(null, "Marque cuando material devuelto + scrap sea menor a lo importado. QMM deberá clasificar la discrepancia (VAL-050)."),
        ["GreenConeReported"] = new("Categoría \"NO REPORTADO CON CONO VERDE\"", "Evidencia: el material se reportó con cono verde."),
        ["PlatformReported"] = new("Categoría \"…NO REPORTADO (PLATAFORMA, CONO, RMA)\"", "Evidencia: el material se reportó en plataforma."),
        ["RmaNumber"] = new(null, "Número de RMA, si aplica."),
        // ---- Etapa 4 · Cierre ----
        ["LowExportNumber"] = new("LOW export", "Número de expo de LOW (antes texto \"Incluido en expo #4…\").", "4"),
        ["LowExportDate"] = new("LOW export", "Fecha de la expo de LOW."),
        ["LowExportNote"] = new("LOW export", "Nota libre de LOW."),
        ["CustomsExportReference"] = new("Customs Export", "PRyy-##### (exportación), F4 (cambio de régimen) o V1 (pedimento virtual). Textos o fórmulas NO cierran (VAL-046).", "PR26-00101"),
        ["CustomsExportDate"] = new(null, "Fecha de la exportación aduanal. No anterior a la expo de LOW."),
        ["ScrapGrNumber"] = new("GR Number", "Número de GR del scrap, 10 dígitos (VAL-044). Lo captura MFO.", "4951422891"),
        ["HseScrapKg"] = new("Kg Qty from HSE / Qty (Kg). from HSE", "Kilos destruidos, mayor a 0 (VAL-045).", "0.5"),
        ["HseConfirmationDate"] = new("HSE confirmation Date", "Con GR + kg + fecha, el scrap sale del balance. No anterior a la fecha de scrap (VAL-043)."),
        // ---- Etapa 5 · Clasificación ----
        ["StatusCategoryCode"] = new("COMMENTS / Date - Name: / Column2", "EXPORT, SCRAP e INVENTORY se asignan solas; las discrepancias las asigna QMM con su área responsable."),
        ["Comments"] = new("COMMENTS", "Comentario libre."),
        ["IsCancelled"] = new(null, "Sólo Admin, y sólo si el pallet no cruzó la frontera (VAL-052)."),
        ["CancelReason"] = new(null, "Obligatorio al cancelar."),
        // ---- Indicadores ----
        ["MfoInventoryUsd"] = new("MFO INVENTORY USD", "Piezas aún en piso MFO × costo."),
        ["HseScrapUsd"] = new("HSE SCRAP USD", "Scrap sin confirmación HSE × costo."),
        ["MfoLowFailureUsd"] = new("MFO-LOW FAILURE USD", "(En LOW sin pedimento + faltantes) × costo."),
        ["BalanceGeneralUsd"] = new("Balance GENERAL DLLS", "Suma de los tres indicadores anteriores: valor aún no conciliado."),
        ["DutiesToPayUsd"] = new("DUTIES TO PAY USD", "Falla MFO-LOW × 0.19 (tasa provisional)."),
        ["DutyExposureUsd"] = new(null, "Balance × 0.19: aranceles a pagar si no se concilia."),
    };

    public static string Get(string field)
    {
        if (!Map.TryGetValue(field, out var e)) return field;
        var parts = new List<string>();
        if (e.Excel is not null) parts.Add($"Excel 2.0: {e.Excel}");
        parts.Add(e.Rule);
        if (e.Example is not null) parts.Add($"Ejemplo: {e.Example}");
        return string.Join(Environment.NewLine, parts);
    }
}

/// <summary>Indexador para ToolTip="{Binding Help[Campo]}".</summary>
public sealed class HelpMap
{
    public static HelpMap Instance { get; } = new();
    public string this[string field] => FieldHelp.Get(field);
}

/// <summary>Etiquetas en español por campo (UX-B03). Se usan en el formulario, mensajes y bitácora.</summary>
public static class FieldLabels
{
    /// <summary>Obligatorios para pasar a "Solicitado" (UX-B04).</summary>
    public static readonly HashSet<string> Required = new()
    {
        "QmmResponsible", "RequestingDepartment", "RequestorName", "ReasonId", "ReturnedMaterial", "QtyImported", "OriginalHU", "OriginalBatch",
    };

    private static readonly Dictionary<string, string> Map = new()
    {
        ["ValueStreamId"] = "Value Stream (VS)", ["FolioNumber"] = "Folio",
        ["QmmResponsible"] = "Responsable QMM (Champion / Proxy)", ["RequestingDepartment"] = "Departamento solicitante",
        ["RequestorName"] = "Solicitante (Apellido, Nombre)", ["ReasonId"] = "Motivo oficial",
        ["ReturnReasonDetail"] = "Detalle del motivo", ["ReturnedMaterial"] = "Número de parte devuelto (13 dígitos)",
        ["UnitCostUsd"] = "Costo unitario MM60 (USD)", ["SapUsdTotal"] = "Valor total SAP (USD, calculado)",
        ["QtyImported"] = "Cantidad importada", ["OriginalHU"] = "HU original (10 dígitos)", ["OriginalBatch"] = "Lote original",
        ["DateReturnedToWhs"] = "Fecha PO / retorno a almacén", ["ImportDate"] = "Fecha de importación",
        ["ImportInvoice"] = "Factura de importación", ["ImportDateElpJup1"] = "Fecha de cruce El Paso → JuP1",
        ["ImportPedimento"] = "Pedimento de importación", ["DutyPaymentStatus"] = "Estatus de pago de aranceles",
        ["ReturnPONumber"] = "PO de retorno (8 dígitos)", ["ReturnPOItem"] = "Posición de la PO", ["DeliveryNumber"] = "Número de entrega (LOP)",
        ["IbOrderTypeCode"] = "Tipo de orden IB", ["IbOrderNumber"] = "Número de orden IB", ["NewMaterial"] = "Número de parte nuevo",
        ["NewHU"] = "HU nuevo", ["ReworkBatch"] = "Lote del retrabajo", ["ReinsertionQty"] = "Material reinsertado (cantidad)",
        ["OldMaterialDeliveredQty"] = "Material original devuelto a LOW", ["ScrapQty"] = "Cantidad a scrap (HSE)", ["ScrapDate"] = "Fecha de scrap",
        ["PhysicalDeliveredQty"] = "Entregado físicamente a LOW", ["PhysicalDeliveredDate"] = "Fecha de entrega física a LOW",
        ["DateSentBack"] = "Fecha de regreso", ["DeclaredMissing"] = "Declarar faltante", ["GreenConeReported"] = "Reportado con cono verde",
        ["PlatformReported"] = "Reportado en plataforma", ["RmaNumber"] = "RMA",
        ["LowExportNumber"] = "Expo LOW · número", ["LowExportDate"] = "Expo LOW · fecha", ["LowExportNote"] = "Expo LOW · nota",
        ["CustomsExportReference"] = "Exportación aduanal (PRyy-##### · F4 · V1)", ["CustomsExportDate"] = "Fecha de exportación aduanal",
        ["ScrapGrNumber"] = "Número de GR (MFO)", ["HseScrapKg"] = "Kg destruidos (HSE)", ["HseConfirmationDate"] = "Fecha de confirmación HSE",
        ["StatusCategoryCode"] = "Categoría de estatus", ["Comments"] = "Comentarios", ["IsCancelled"] = "Cancelado",
        ["CancelReason"] = "Motivo de cancelación", ["Status"] = "Estatus", ["Registro"] = "Registro",
    };

    public static string Get(string field) => Map.TryGetValue(field, out var l) ? l : field;
    public static string Label(string field) => Required.Contains(field) ? $"{Get(field)} *" : Get(field);
}

/// <summary>Muestra la etiqueta en español del campo en tablas (bitácora).</summary>
public sealed class FieldLabelConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is string f ? FieldLabels.Get(f) : value;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
