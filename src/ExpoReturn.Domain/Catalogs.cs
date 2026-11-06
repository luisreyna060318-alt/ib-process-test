namespace ExpoReturn.Domain;

public sealed record ValueStream(int Id, string Code, string Name);
/// <summary>Description = texto oficial del Excel (inglés, se usa para importar); Label = etiqueta en español.</summary>
public sealed record Reason(int Id, string Code, string Description, string Group, bool RequiresNewMaterial, string Label)
{
    public string ExcelHelp => $"Excel 2.0: {Description}";
}
public sealed record Department(string Code, string Name, string Area, bool CanRequest);
public sealed record StatusCategory(string Code, string Description, string Kind, string? ResponsibleArea, bool AffectsDuty, bool IsAutoDerived);
public sealed record IbOrderType(string Code, string Description);

/// <summary>Catálogos oficiales (semilla de 02_seed_catalogs.sql). Fuente: listas del Excel 2.0 real.</summary>
public static class Catalogs
{
    public static IReadOnlyList<ValueStream> ValueStreams { get; } = new[]
    {
        new ValueStream(1, "VS1", "Value Stream 1"),
        new ValueStream(4, "VS4", "Value Stream 4"),
        new ValueStream(5, "VS5", "Value Stream 5"),
        new ValueStream(7, "VS7", "Value Stream 7"),
        new ValueStream(9, "VS9", "Value Stream 9"),
    };

    /// <summary>El Excel real trae 10 motivos (la especificación decía 9). [VALIDAR QMM]</summary>
    public static IReadOnlyList<Reason> Reasons { get; } = new[]
    {
        new Reason(1, "R01", "Packaging change due to customer request", "PACKAGING", false, "Cambio de empaque por solicitud del cliente"),
        new Reason(2, "R02", "Packaging change due to planning error", "PACKAGING", false, "Cambio de empaque por error de planeación"),
        new Reason(3, "R03", "To change FG P/N with reflash", "REFLASH", true, "Cambio de número de parte FG con reflasheo"),
        new Reason(4, "R04", "To inspect due to possible pallet damage", "INSPECTION", false, "Inspección por posible daño del pallet"),
        new Reason(5, "R05", "To complete a partial pallet", "COMPLETION", false, "Completar un pallet parcial"),
        new Reason(6, "R06", "Inspect due to Customer Quality", "INSPECTION", false, "Inspección por calidad del cliente"),
        new Reason(7, "R07", "Inspect due to Manufacturing Quality", "INSPECTION", false, "Inspección por calidad de manufactura"),
        new Reason(8, "R08", "Inspect due to Supplier Quality", "INSPECTION", false, "Inspección por calidad del proveedor"),
        new Reason(9, "R09", "Inspect due to incomplete box or pallet", "INSPECTION", false, "Inspección por caja o pallet incompleto"),
        new Reason(10, "R10", "Inspect due to mistakes in standard pack", "PACKAGING", false, "Inspección por errores en el empaque estándar"),
    };

    public static IReadOnlyList<Department> Departments { get; } = BuildDepartments();

    private static IReadOnlyList<Department> BuildDepartments()
    {
        var list = new List<Department>
        {
            new("QMM", "Quality Management", "QMM", true),
            new("QMM-C1", "QMM Customer Quality 1", "QMM", true),
            new("QMM-C2", "QMM Customer Quality 2", "QMM", true),
            new("MFQ", "Manufacturing Quality", "MFQ", true),
        };
        for (var i = 1; i <= 9; i++) list.Add(new($"MFQ{i}", $"Manufacturing Quality {i}", "MFQ", true));
        list.Add(new("MFO", "Manufacturing Operations", "MFO", true));
        foreach (var i in new[] { 1, 4, 5, 7, 9 }) list.Add(new($"MFO{i}", $"Manufacturing Operations {i}", "MFO", true));
        list.Add(new("LOP", "Logistics Planning", "LOP", true));
        list.Add(new("LOP1", "Logistics Planning 1", "LOP", true));
        list.Add(new("LOP2", "Logistics Planning 2", "LOP", true));
        list.Add(new("LOW", "Logistics Warehouse", "LOW", false));
        list.Add(new("GS/OSD43", "Comercio Exterior / Aduanas", "GS", false));
        list.Add(new("HSE", "Health, Safety & Environment", "HSE", false));
        list.Add(new("PUQ", "Purchasing Quality", "PUQ", true));
        return list;
    }

    public static IReadOnlyList<StatusCategory> StatusCategories { get; } = new[]
    {
        new StatusCategory("EXPORT", "Exportado a El Paso WHS", "OUTCOME", null, false, true),
        new StatusCategory("SCRAP", "Destruido vía HSE", "OUTCOME", null, false, true),
        new StatusCategory("INVENTORY", "Inventario físico abierto en JuP1", "OUTCOME", null, false, true),
        new StatusCategory("ENVIO_NACIONAL", "Envío nacional (descargo por otra vía)", "OUTCOME", "GS", false, false),
        new StatusCategory("SCRAP_SIN_SALIDA_FISICA", "Scrap sin salida física", "DISCREPANCY", "HSE", true, false),
        new StatusCategory("NO_REPORTADO_CORRECTAMENTE", "No reportado correctamente", "DISCREPANCY", null, true, false),
        new StatusCategory("NO_REPORTADO_CONO_VERDE", "No reportado con cono verde", "DISCREPANCY", "MFO", true, false),
        new StatusCategory("NO_LLEGO_CON_CONO", "No llegó con cono", "DISCREPANCY", "LOP", true, false),
        new StatusCategory("CONO_NO_REPORTADO_POR_LOW", "Material con cono no reportado por LOW", "DISCREPANCY", "LOW", true, false),
        new StatusCategory("MATERIAL_NO_LLEGO_DE_ELP", "Material no llegó de El Paso", "DISCREPANCY", "LOP", true, false),
        new StatusCategory("LOW_NO_REPORTADO_PCR", "LOW - no reportado (plataforma, cono, RMA)", "DISCREPANCY", "LOW", true, false),
        new StatusCategory("MFO_NO_REPORTADO_PCR", "MFO - no reportado (plataforma, cono, RMA)", "DISCREPANCY", "MFO", true, false),
    };

    public static IReadOnlyList<IbOrderType> IbOrderTypes { get; } = new[]
    {
        new IbOrderType("PPI2", "Inhouse repair (estándar)"),
        new IbOrderType("PPI3", "Falla con costo"),
        new IbOrderType("PPI4", "Garantía"),
        new IbOrderType("PPI5", "Daño de transporte"),
        new IbOrderType("PPI6", "Daño de transporte"),
    };

    public static Reason? FindReason(int? id) => id is null ? null : Reasons.FirstOrDefault(r => r.Id == id);
    public static StatusCategory? FindCategory(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : StatusCategories.FirstOrDefault(c => c.Code == code);
}
