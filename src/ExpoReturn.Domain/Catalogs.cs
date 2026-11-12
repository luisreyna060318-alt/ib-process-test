namespace ExpoReturn.Domain;

public sealed record ValueStream(int Id, string Code, string Name, bool IsActive = true);
/// <summary>Description = official text of the Excel list (also used to import). IsActive = false: kept for existing records.</summary>
public sealed record Reason(int Id, string Code, string Description, string Group, bool RequiresNewMaterial, bool IsActive = true)
{
    public string Label => Description;
}
/// <summary>
/// Department code (e.g. MFQ8) and its AREA, which decides the fields its Champions / Proxies edit (MFQ8 → MFQ).
/// Group: PROCESS (departments of the return flow) or CROSS_FUNCTIONAL (digitalization and cross-functional projects).
/// IsActive = false: code that only appears in historical records (kept so old rows stay valid; not offered any more).
/// </summary>
public sealed record Department(string Code, string Area, bool CanRequest, string Group = Department.Process, bool IsActive = true)
{
    public const string Process = "PROCESS";
    public const string CrossFunctional = "CROSS_FUNCTIONAL";
    public string Label => Group == CrossFunctional ? $"{Code}  (digitalization / cross-functional)" : Code;
}
public sealed record StatusCategory(string Code, string Description, string Kind, string? ResponsibleArea, bool AffectsDuty, bool IsAutoDerived,
                                    bool IsActive = true);

/// <summary>
/// Catalogs read from the database (IB_PROCESS_VALUE_STREAMS, _REASONS, _STATUS_CATEGORIES, _STATUS_CAT_VS,
/// _DEPARTMENTS, _WORKFLOW_STATUS). An empty part keeps what was there.
/// </summary>
public sealed record CatalogData(
    IReadOnlyList<ValueStream> ValueStreams,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<StatusCategory> StatusCategories,
    IReadOnlyDictionary<int, IReadOnlyList<string>> StatusCategoriesByVs,
    IReadOnlyList<Department> Departments,
    IReadOnlyDictionary<WorkflowStatus, string> WorkflowStatusNames);
public sealed record IbOrderType(string Code, string Description);

/// <summary>
/// Catalogs. The application reads them from the database at start-up and on Reload (<see cref="Use"/>), so they are
/// managed there. The built-in lists (= 02_seed_data.sql) are only the starting point and what the tests use.
/// </summary>
public static class Catalogs
{
    private static volatile IReadOnlyList<ValueStream>? _valueStreams;
    private static volatile IReadOnlyList<Reason>? _reasons;
    private static volatile IReadOnlyList<StatusCategory>? _statusCategories;
    private static volatile IReadOnlyDictionary<int, IReadOnlyList<string>>? _statusCategoriesByVs;
    private static volatile IReadOnlyDictionary<WorkflowStatus, string>? _workflowNames;

    /// <summary>Takes the catalogs read from the database. Parts that came back empty keep the current list.</summary>
    public static void Use(CatalogData data)
    {
        if (data.ValueStreams.Count > 0) _valueStreams = data.ValueStreams;
        if (data.Reasons.Count > 0) _reasons = data.Reasons;
        if (data.StatusCategories.Count > 0) _statusCategories = data.StatusCategories;
        if (data.StatusCategoriesByVs.Count > 0) _statusCategoriesByVs = data.StatusCategoriesByVs;
        if (data.WorkflowStatusNames.Count > 0) _workflowNames = data.WorkflowStatusNames;
        UseDepartments(data.Departments);
    }

    /// <summary>Back to the built-in lists (tests).</summary>
    public static void UseBuiltIn()
    {
        _valueStreams = null; _reasons = null; _statusCategories = null; _statusCategoriesByVs = null; _workflowNames = null;
        UseBuiltInDepartments();
    }

    /// <summary>Name of a status as the database calls it (IB_PROCESS_WORKFLOW_STATUS.NAME); null = built-in name.</summary>
    public static string? WorkflowStatusName(WorkflowStatus s) => _workflowNames?.GetValueOrDefault(s);

    /// <summary>Active Value Streams (offered in the screens).</summary>
    public static IReadOnlyList<ValueStream> ValueStreams => AllValueStreams.Where(v => v.IsActive).ToList();
    public static IReadOnlyList<ValueStream> AllValueStreams => _valueStreams ?? BuiltInValueStreams;

    public static IReadOnlyList<ValueStream> BuiltInValueStreams { get; } = new[]
    {
        new ValueStream(1, "VS1", "Value Stream 1"),
        new ValueStream(4, "VS4", "Value Stream 4"),
        new ValueStream(5, "VS5", "Value Stream 5"),
        new ValueStream(7, "VS7", "Value Stream 7"),
        new ValueStream(9, "VS9", "Value Stream 9"),
    };

    /// <summary>Active reasons (offered). Inactive ones are still found by <see cref="FindReason"/> for existing records.</summary>
    public static IReadOnlyList<Reason> Reasons => AllReasons.Where(r => r.IsActive).ToList();
    public static IReadOnlyList<Reason> AllReasons => _reasons ?? BuiltInReasons;

    /// <summary>The 10 reasons of the real Excel (the specification said 9). [VALIDATE QMM]</summary>
    public static IReadOnlyList<Reason> BuiltInReasons { get; } = new[]
    {
        new Reason(1, "R01", "Packaging change due to customer request", "PACKAGING", false),
        new Reason(2, "R02", "Packaging change due to planning error", "PACKAGING", false),
        new Reason(3, "R03", "To change FG P/N with reflash", "REFLASH", true),
        new Reason(4, "R04", "To inspect due to possible pallet damage", "INSPECTION", false),
        new Reason(5, "R05", "To complete a partial pallet", "COMPLETION", false),
        new Reason(6, "R06", "Inspect due to Customer Quality", "INSPECTION", false),
        new Reason(7, "R07", "Inspect due to Manufacturing Quality", "INSPECTION", false),
        new Reason(8, "R08", "Inspect due to Supplier Quality", "INSPECTION", false),
        new Reason(9, "R09", "Inspect due to incomplete box or pallet", "INSPECTION", false),
        new Reason(10, "R10", "Inspect due to mistakes in standard pack", "PACKAGING", false),
    };

    /// <summary>
    /// Departments offered in the application (list agreed with the process owner, plus GS/OSD43 and HSE, which take part in
    /// the flow). [VALIDATE] areas of MFE / TEF / CTG / DBE / QMM-L and the last cross-functional department of the list.
    /// </summary>
    public static IReadOnlyList<Department> Departments => AllDepartments.Where(d => d.IsActive).ToList();

    private static volatile IReadOnlyList<Department>? _departments;     // null = built-in list

    /// <summary>
    /// Departments in use. With the database they come from IB_PROCESS_DEPARTMENTS (see <see cref="UseDepartments"/>), so
    /// activating or adding one there needs no new executable. Without it: <see cref="BuiltInDepartments"/>.
    /// </summary>
    public static IReadOnlyList<Department> AllDepartments => _departments ?? BuiltInDepartments;

    /// <summary>Replaces the department list (read from the database at start-up and on Reload). An empty list is ignored.</summary>
    public static void UseDepartments(IEnumerable<Department> departments)
    {
        var list = departments.ToList();
        if (list.Count > 0) _departments = list;
    }

    /// <summary>Back to the built-in list (Local mode, tests).</summary>
    public static void UseBuiltInDepartments() => _departments = null;

    /// <summary>Built-in list = the seed of IB_PROCESS_DEPARTMENTS (02_seed_data.sql). Used in Local mode.</summary>
    public static IReadOnlyList<Department> BuiltInDepartments { get; } = new Department[]
    {
        // Process departments (JuP1/...)
        new("MFQ8", "MFQ", true),
        new("MFO1", "MFO", true),
        new("MFO12", "MFO", true),
        new("MFO1-US", "MFO", true),
        new("MFE5", "MFE", true),
        new("QMM1", "QMM", true),
        new("QMM2", "QMM", true),
        new("LOP1", "LOP", true),
        new("LOP2", "LOP", true),
        new("LOW", "LOW", false),
        new("CTG3", "CTG", false),
        new("TEF6", "TEF", true),
        new("GS/OSD43", "GS", false),
        new("HSE", "HSE", false),
        // Requesting departments of the Excel 2.0 list (sheet "Sheet1") and LOW2, found in the Excel rows
        new("MFQ1", "MFQ", true), new("MFQ4", "MFQ", true), new("MFQ5", "MFQ", true), new("MFQ7", "MFQ", true), new("MFQ9", "MFQ", true),
        new("QMM-C1", "QMM", true), new("QMM-C2", "QMM", true), new("PUQ", "PUQ", true), new("LOW2", "LOW", true),
        // Digitalization and cross-functional projects
        new("QMM-L2", "QMM-L", false, Department.CrossFunctional),
        new("DBE", "DBE", false, Department.CrossFunctional),
        new("TEF1", "TEF", false, Department.CrossFunctional),
        // Old list: history only
        new("QMM", "QMM", true, IsActive: false),
        new("MFQ", "MFQ", true, IsActive: false),
        new("MFQ2", "MFQ", true, IsActive: false), new("MFQ3", "MFQ", true, IsActive: false), new("MFQ6", "MFQ", true, IsActive: false),
        new("MFO", "MFO", true, IsActive: false),
        new("MFO4", "MFO", true, IsActive: false), new("MFO5", "MFO", true, IsActive: false),
        new("MFO7", "MFO", true, IsActive: false), new("MFO9", "MFO", true, IsActive: false),
        new("LOP", "LOP", true, IsActive: false),
    };

    public static Department? FindDepartment(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null
        : AllDepartments.FirstOrDefault(d => string.Equals(d.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opciones de la columna COMMENTS, con el texto exacto del Excel. Code = texto que se guarda.
    /// Kind/área responsable/efecto en aranceles alimentan las reglas (riesgo arancelario, categoría automática).
    /// </summary>
    public static IReadOnlyList<StatusCategory> StatusCategories => _statusCategories ?? BuiltInStatusCategories;

    public static IReadOnlyList<StatusCategory> BuiltInStatusCategories { get; } = new[]
    {
        Cat("EXPORT", "OUTCOME", null, false, true),
        Cat("SCRAP", "OUTCOME", null, false, true),
        Cat("INVENTORY", "OUTCOME", null, false, true),
        Cat("ENVIO NACIONAL", "OUTCOME", "GS", false, false),
        Cat("SCRAP SIN SALIDA FISICA", "DISCREPANCY", "HSE", true, false),
        Cat("NO REPORTADO CORRECTAMENTE", "DISCREPANCY", null, true, false),
        Cat("NO REPORTADO CON CONO VERDE", "DISCREPANCY", "MFO", true, false),
        Cat("NO LLEGO CON CONO", "DISCREPANCY", "LOP", true, false),
        Cat("MATERIAL CON CONO NO REPORTADO POR LOW", "DISCREPANCY", "LOW", true, false),
        Cat("MATERIAL NO LLEGO DE ELP", "DISCREPANCY", "LOP", true, false),
        Cat("LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "DISCREPANCY", "LOW", true, false),
        Cat("MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)", "DISCREPANCY", "MFO", true, false),
    };

    private static StatusCategory Cat(string text, string kind, string? area, bool affectsDuty, bool auto) =>
        new(text, text, kind, area, affectsDuty, auto);

    /// <summary>
    /// Lista desplegable de COMMENTS de cada hoja VS, tal como está en la validación de datos del Excel 2.0
    /// (cada hoja tiene su propia lista).
    /// </summary>
    private static readonly Dictionary<int, string[]> BuiltInCommentsListByVs = new()
    {
        [1] = new[] { "EXPORT", "SCRAP", "INVENTORY", "LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)",
                      "ENVIO NACIONAL", "SCRAP SIN SALIDA FISICA" },
        [4] = new[] { "NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
                      "MATERIAL CON CONO NO REPORTADO POR LOW", "ENVIO NACIONAL", "MATERIAL NO LLEGO DE ELP" },
        [5] = new[] { "NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
                      "MATERIAL CON CONO NO REPORTADO POR LOW" },
        [7] = new[] { "NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
                      "MATERIAL CON CONO NO REPORTADO POR LOW" },
        [9] = new[] { "NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
                      "MATERIAL CON CONO NO REPORTADO POR LOW", "ENVIO NACIONAL" },
    };

    /// <summary>Opciones de COMMENTS para un VS; si el registro ya trae otro valor (p. ej. histórico), se agrega para no perderlo.</summary>
    public static IReadOnlyList<string> CommentsOptions(int valueStreamId, string? current = null)
    {
        var active = StatusCategories.Where(c => c.IsActive).Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string>? perVs = _statusCategoriesByVs is { } db ? db.GetValueOrDefault(valueStreamId)
            : BuiltInCommentsListByVs.TryGetValue(valueStreamId, out var l) ? l : null;
        var list = (perVs ?? StatusCategories.Select(c => c.Code)).Where(active.Contains).ToList();
        if (!string.IsNullOrWhiteSpace(current) && !list.Contains(current)) list.Add(current);
        return list;
    }

    public static Reason? FindReason(int? id) => id is null ? null : AllReasons.FirstOrDefault(r => r.Id == id);
    public static Reason? FindReasonByText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : AllReasons.FirstOrDefault(r => r.Description.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
    public static StatusCategory? FindCategory(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null
        : StatusCategories.FirstOrDefault(c => string.Equals(c.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
