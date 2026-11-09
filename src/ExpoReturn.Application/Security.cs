using ExpoReturn.Domain;

namespace ExpoReturn.Application;

public enum RoleCode { AdminQmmPe, Qmm, Requestor, Gs, Lop, Mfo, Low, Hse, Viewer }

public static class RoleInfo
{
    public static string DisplayName(this RoleCode r) => r switch
    {
        RoleCode.AdminQmmPe => "Admin QMM / Process Excellence",
        RoleCode.Qmm => "QMM Champion/Proxy",
        RoleCode.Requestor => "Solicitante (MFQ / QMM-C / LOP / PUQ)",
        RoleCode.Gs => "Comercio Exterior GS/OSD43",
        RoleCode.Lop => "Logística de Planta (LOP)",
        RoleCode.Mfo => "Manufactura (MFO)",
        RoleCode.Low => "Almacén (LOW)",
        RoleCode.Hse => "HSE",
        _ => "Consulta",
    };

    /// <summary>Nombre corto del área en la matriz Champions VS.</summary>
    public static string ShortName(this RoleCode r) => r switch
    {
        RoleCode.Mfo => "MFO", RoleCode.Qmm => "QMM", RoleCode.Requestor => "MFQ", RoleCode.Gs => "GS / OSD43",
        RoleCode.Lop => "LOP", RoleCode.Low => "LOW", RoleCode.Hse => "HSE", RoleCode.AdminQmmPe => "Admin", _ => "Consulta",
    };
}

/// <summary>Usuario actual. En producción sale de Active Directory + Champions_Matrix; en el borrador se simula.</summary>
public sealed record UserContext(string NtUser, string DisplayName, RoleCode Role, IReadOnlyList<int> ValueStreams)
{
    public bool IsAdmin => Role == RoleCode.AdminQmmPe;
    /// <summary>Sólo Admin y Consulta ven todos los VS; las demás áreas quedan delimitadas por la matriz Champions VS.</summary>
    public bool SeesAllValueStreams => Role is RoleCode.AdminQmmPe or RoleCode.Viewer;
    public bool CoversValueStream(int vs) => IsAdmin || ValueStreams.Contains(vs);
    public string ValueStreamText => SeesAllValueStreams ? "Todos los VS"
        : ValueStreams.Count == 0 ? "Sin VS asignado" : string.Join(", ", ValueStreams.Select(v => $"VS{v}"));
    public string Label => $"{DisplayName} · {Role.DisplayName()} · {ValueStreamText}";
    public string AreaText => SeesAllValueStreams ? Role.DisplayName() : Role.ShortName();
    /// <summary>Grupo en el selector de usuario simulado.</summary>
    public string Group => SeesAllValueStreams ? "General" : Role.DisplayName();
}

/// <summary>Campos editables (nombres = propiedades de PalletReturn = columnas SQL).</summary>
public static class Fields
{
    // Campos = columnas del Excel 2.0, en su orden. "Date PO" y "PO Number" se capturan en la Solicitud.
    public static readonly string[] Stage1 =
    {
        nameof(PalletReturn.QmmResponsible), nameof(PalletReturn.RequestingDepartment), nameof(PalletReturn.RequestorName),
        nameof(PalletReturn.ReasonId), nameof(PalletReturn.ReturnReasonDetail), nameof(PalletReturn.ReturnedMaterial),
        nameof(PalletReturn.QtyImported), nameof(PalletReturn.OriginalHU), nameof(PalletReturn.OriginalBatch),
        nameof(PalletReturn.DateReturnedToWhs), nameof(PalletReturn.ReturnPONumber),
    };
    public static readonly string[] Stage2 =
    {
        nameof(PalletReturn.ImportDate), nameof(PalletReturn.ImportInvoice), nameof(PalletReturn.ImportDateElpJup1),
        nameof(PalletReturn.DeliveryNumber),
    };
    public static readonly string[] Stage3 =
    {
        nameof(PalletReturn.NewMaterial), nameof(PalletReturn.PhysicalDeliveredQty), nameof(PalletReturn.NewHU),
        nameof(PalletReturn.ReworkBatch), nameof(PalletReturn.ReinsertionQty), nameof(PalletReturn.DateSentBack),
        nameof(PalletReturn.OldMaterialDeliveredQty), nameof(PalletReturn.ScrapQty), nameof(PalletReturn.ScrapDate),
    };
    public static readonly string[] Stage4 =
    {
        nameof(PalletReturn.LowExport), nameof(PalletReturn.CustomsExportReference), nameof(PalletReturn.ScrapGrNumber),
        nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate),
    };
    public static readonly string[] Stage5 = { nameof(PalletReturn.StatusCategoryCode) };
    public static IEnumerable<string> All => Stage1.Concat(Stage2).Concat(Stage3).Concat(Stage4).Concat(Stage5);
}

/// <summary>Matriz rol × campo (sec.Field_Permissions). Replica los encabezados de área del Excel.</summary>
public static class FieldPermissions
{
    /// <summary>Fila de áreas del Excel: QMM (Solicitud, Date PO, PO Number, COMMENTS), GS/OSD43 (importación y Customs Export),
    /// LOP (Delivery Number), MFO (ejecución y GR Number), LOW (LOW export), HSE (kg y confirmación).</summary>
    private static readonly Dictionary<RoleCode, HashSet<string>> Map = new()
    {
        [RoleCode.Qmm] = new(Fields.Stage1.Append(nameof(PalletReturn.StatusCategoryCode))),
        [RoleCode.Requestor] = new(Fields.Stage1.Except(new[]
        {
            nameof(PalletReturn.QmmResponsible), nameof(PalletReturn.DateReturnedToWhs), nameof(PalletReturn.ReturnPONumber),
        })),
        [RoleCode.Gs] = new(new[]
        {
            nameof(PalletReturn.ImportDate), nameof(PalletReturn.ImportInvoice), nameof(PalletReturn.ImportDateElpJup1),
            nameof(PalletReturn.CustomsExportReference),
        }),
        [RoleCode.Lop] = new(new[] { nameof(PalletReturn.DeliveryNumber) }),
        [RoleCode.Mfo] = new(Fields.Stage3.Append(nameof(PalletReturn.ScrapGrNumber))),
        [RoleCode.Low] = new(new[] { nameof(PalletReturn.LowExport) }),
        [RoleCode.Hse] = new(new[] { nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate) }),
        [RoleCode.Viewer] = new(),
    };

    public static bool CanEdit(UserContext user, PalletReturn pallet, string field)
    {
        if (user.IsAdmin) return true;
        if (!user.CoversValueStream(pallet.ValueStreamId)) return false;
        if (pallet.Status.IsClosed() || pallet.IsLegacy) return false;
        return Map.TryGetValue(user.Role, out var set) && set.Contains(field);
    }

    public static IReadOnlyCollection<string> EditableFields(RoleCode role) =>
        role == RoleCode.AdminQmmPe ? Fields.All.ToArray() : Map.TryGetValue(role, out var s) ? s : Array.Empty<string>();
}
