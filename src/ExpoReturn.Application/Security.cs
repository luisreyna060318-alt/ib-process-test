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
}

/// <summary>Usuario actual. En producción sale de Active Directory + Champions_Matrix; en el borrador se simula.</summary>
public sealed record UserContext(string NtUser, string DisplayName, RoleCode Role, IReadOnlyList<int> ValueStreams)
{
    public bool IsAdmin => Role == RoleCode.AdminQmmPe;
    public bool CoversValueStream(int vs) => IsAdmin || ValueStreams.Count == 0 || ValueStreams.Contains(vs);
    public string Label => $"{DisplayName} · {Role.DisplayName()} · {(ValueStreams.Count == 0 || IsAdmin ? "Todos los VS" : string.Join(", ", ValueStreams.Select(v => $"VS{v}")))}";
}

/// <summary>Campos editables (nombres = propiedades de PalletReturn = columnas SQL).</summary>
public static class Fields
{
    public static readonly string[] Stage1 =
    {
        nameof(PalletReturn.QmmResponsible), nameof(PalletReturn.RequestingDepartment), nameof(PalletReturn.RequestorName),
        nameof(PalletReturn.ReasonId), nameof(PalletReturn.ReturnReasonDetail), nameof(PalletReturn.ReturnedMaterial),
        nameof(PalletReturn.QtyImported), nameof(PalletReturn.OriginalHU), nameof(PalletReturn.OriginalBatch),
    };
    public static readonly string[] Stage2 =
    {
        nameof(PalletReturn.DateReturnedToWhs), nameof(PalletReturn.ImportDate), nameof(PalletReturn.ImportInvoice),
        nameof(PalletReturn.ImportDateElpJup1), nameof(PalletReturn.ImportPedimento), nameof(PalletReturn.ReturnPONumber),
        nameof(PalletReturn.ReturnPOItem), nameof(PalletReturn.DeliveryNumber), nameof(PalletReturn.DutyPaymentStatus),
    };
    public static readonly string[] Stage3 =
    {
        nameof(PalletReturn.IbOrderNumber), nameof(PalletReturn.IbOrderTypeCode), nameof(PalletReturn.PhysicalDeliveredQty),
        nameof(PalletReturn.PhysicalDeliveredDate), nameof(PalletReturn.NewMaterial), nameof(PalletReturn.NewHU),
        nameof(PalletReturn.ReworkBatch), nameof(PalletReturn.ReinsertionQty), nameof(PalletReturn.DateSentBack),
        nameof(PalletReturn.OldMaterialDeliveredQty), nameof(PalletReturn.ScrapQty), nameof(PalletReturn.ScrapDate),
        nameof(PalletReturn.DeclaredMissing), nameof(PalletReturn.GreenConeReported), nameof(PalletReturn.PlatformReported),
        nameof(PalletReturn.RmaNumber),
    };
    public static readonly string[] Stage4 =
    {
        nameof(PalletReturn.LowExportNumber), nameof(PalletReturn.LowExportDate), nameof(PalletReturn.LowExportNote),
        nameof(PalletReturn.CustomsExportReference), nameof(PalletReturn.CustomsExportDate), nameof(PalletReturn.ScrapGrNumber),
        nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate),
    };
    public static readonly string[] Stage5 =
    {
        nameof(PalletReturn.StatusCategoryCode), nameof(PalletReturn.Comments), nameof(PalletReturn.IsCancelled), nameof(PalletReturn.CancelReason),
    };
    public static IEnumerable<string> All => Stage1.Concat(Stage2).Concat(Stage3).Concat(Stage4).Concat(Stage5);
}

/// <summary>Matriz rol × campo (sec.Field_Permissions). Replica los encabezados de área del Excel.</summary>
public static class FieldPermissions
{
    private static readonly Dictionary<RoleCode, HashSet<string>> Map = new()
    {
        [RoleCode.Qmm] = new(Fields.Stage1.Concat(new[]
        {
            nameof(PalletReturn.DateReturnedToWhs), nameof(PalletReturn.ReturnPONumber), nameof(PalletReturn.ReturnPOItem),
            nameof(PalletReturn.StatusCategoryCode), nameof(PalletReturn.Comments),
        })),
        [RoleCode.Requestor] = new(Fields.Stage1.Except(new[] { nameof(PalletReturn.QmmResponsible) })),
        [RoleCode.Gs] = new(new[]
        {
            nameof(PalletReturn.DateReturnedToWhs), nameof(PalletReturn.ImportDate), nameof(PalletReturn.ImportInvoice),
            nameof(PalletReturn.ImportDateElpJup1), nameof(PalletReturn.ImportPedimento), nameof(PalletReturn.DutyPaymentStatus),
            nameof(PalletReturn.CustomsExportReference), nameof(PalletReturn.CustomsExportDate),
        }),
        [RoleCode.Lop] = new(new[] { nameof(PalletReturn.DeliveryNumber), nameof(PalletReturn.DutyPaymentStatus) }),
        [RoleCode.Mfo] = new(Fields.Stage3.Append(nameof(PalletReturn.ScrapGrNumber))),
        [RoleCode.Low] = new(new[] { nameof(PalletReturn.LowExportNumber), nameof(PalletReturn.LowExportDate), nameof(PalletReturn.LowExportNote) }),
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
