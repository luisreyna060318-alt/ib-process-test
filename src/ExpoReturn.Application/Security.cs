using ExpoReturn.Domain;

namespace ExpoReturn.Application;

/// <summary>
/// Functional area that owns a block of fields (the Excel "Areas responsible to fill out information" row).
/// It comes from the user's department (QMM1 → Qmm, MFQ8/MFE5/TEF6 → Requestor, GS/OSD43 → Gs, …).
/// AdminQmmPe and Viewer are kept for the Owner and read-only users.
/// </summary>
public enum RoleCode { AdminQmmPe, Qmm, Requestor, Gs, Lop, Mfo, Low, Hse, Viewer }

/// <summary>What a user may do in a department and Value Stream. Owner ignores department and VS.</summary>
public enum UserFunction { Viewer = 0, Proxy = 1, Champion = 2, Owner = 3 }

public static class RoleInfo
{
    public static string DisplayName(this RoleCode r) => r switch
    {
        RoleCode.AdminQmmPe => "Owner (QMM / Process Excellence)",
        RoleCode.Qmm => "QMM",
        RoleCode.Requestor => "Requestor (MFQ / MFE / TEF)",
        RoleCode.Gs => "Customs GS/OSD43",
        RoleCode.Lop => "Plant Logistics (LOP)",
        RoleCode.Mfo => "Manufacturing (MFO)",
        RoleCode.Low => "Warehouse (LOW)",
        RoleCode.Hse => "HSE",
        _ => "Viewer",
    };

    /// <summary>Short area name used in the access matrix.</summary>
    public static string ShortName(this RoleCode r) => r switch
    {
        RoleCode.Mfo => "MFO", RoleCode.Qmm => "QMM", RoleCode.Requestor => "MFQ", RoleCode.Gs => "GS / OSD43",
        RoleCode.Lop => "LOP", RoleCode.Low => "LOW", RoleCode.Hse => "HSE", RoleCode.AdminQmmPe => "Owner", _ => "Viewer",
    };

    /// <summary>Area of a department code (catalog Department.Area).</summary>
    public static RoleCode AreaOfDepartment(string? departmentCode)
    {
        return Catalogs.FindDepartment(departmentCode)?.Area switch
        {
            "QMM" => RoleCode.Qmm,
            // Requesting areas: they fill the request block. [VALIDATE] MFE / TEF.
            "MFQ" or "PUQ" or "MFE" or "TEF" => RoleCode.Requestor,
            "GS" => RoleCode.Gs,
            "LOP" => RoleCode.Lop,
            "MFO" => RoleCode.Mfo,
            "LOW" => RoleCode.Low,
            "HSE" => RoleCode.Hse,
            // CTG, DBE, QMM-L (controlling, digitalization, cross-functional): read only. [VALIDATE]
            _ => RoleCode.Viewer,
        };
    }

    public static bool CanEdit(this UserFunction f) => f is UserFunction.Champion or UserFunction.Proxy or UserFunction.Owner;
}

/// <summary>One row of the user access table: user × department × Value Stream × function.</summary>
public sealed record AccessGrant(string DepartmentCode, int ValueStreamId, UserFunction Function)
{
    public RoleCode Area => RoleInfo.AreaOfDepartment(DepartmentCode);
}

/// <summary>
/// The user running the application, read from Windows. Department and Value Streams come from the user's
/// profile; what they can edit comes from the access grants (Champion/Proxy) the Owner assigned.
/// </summary>
public sealed record UserContext(string NtUser, string DisplayName, string? DepartmentCode, bool IsOwner, IReadOnlyList<AccessGrant> Grants)
{
    /// <summary>Owner = full access (department and VS are irrelevant).</summary>
    public bool IsAdmin => IsOwner;
    public IEnumerable<AccessGrant> EditGrants => Grants.Where(g => g.Function is UserFunction.Champion or UserFunction.Proxy);

    /// <summary>Main area (for labels and defaults): first editing grant, else the profile department.</summary>
    public RoleCode Role => IsOwner ? RoleCode.AdminQmmPe
        : EditGrants.Select(g => (RoleCode?)g.Area).FirstOrDefault() ?? RoleCode.Viewer;

    public UserFunction Function => IsOwner ? UserFunction.Owner
        : Grants.Count == 0 ? UserFunction.Viewer : Grants.Max(g => g.Function);

    public IReadOnlyList<int> ValueStreams => Grants.Select(g => g.ValueStreamId).Distinct().OrderBy(v => v).ToList();
    public bool SeesAllValueStreams => IsOwner;
    public bool CoversValueStream(int vs) => IsOwner || EditGrants.Any(g => g.ValueStreamId == vs);
    public bool CanEditArea(RoleCode area, int vs) => IsOwner || EditGrants.Any(g => g.Area == area && g.ValueStreamId == vs);

    public string ValueStreamText => IsOwner ? "All VS"
        : ValueStreams.Count == 0 ? "No VS" : string.Join(", ", ValueStreams.Select(v => $"VS{v}"));
    public string AreaText => IsOwner ? "Owner" : DepartmentCode ?? "—";
    public string FunctionText => Function.ToString();
    public string Label => $"{DisplayName} · {AreaText} · {ValueStreamText} · {FunctionText}";
    public string Group => IsOwner ? "Owner" : RoleInfo.AreaOfDepartment(DepartmentCode).DisplayName();
}

/// <summary>Editable fields (names = PalletReturn properties = database columns), in Excel column order.</summary>
public static class Fields
{
    // "PO Date" and "PO Number" are captured in the Request tab.
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
        nameof(PalletReturn.LowExportNumber), nameof(PalletReturn.LowExportDate), nameof(PalletReturn.CustomsExportReference),
        nameof(PalletReturn.ScrapGrNumber), nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate),
    };
    public static readonly string[] Stage5 = { nameof(PalletReturn.StatusCategoryCode) };
    public static IEnumerable<string> All => Stage1.Concat(Stage2).Concat(Stage3).Concat(Stage4).Concat(Stage5);
}

/// <summary>Area × field matrix (IB_PROCESS_FIELD_PERMISSIONS). Mirrors the Excel area row.</summary>
public static class FieldPermissions
{
    /// <summary>QMM (request, PO date, PO number, status category), GS/OSD43 (import and customs export),
    /// LOP (delivery number), MFO (rework and GR number), LOW (LOW export), HSE (kg and confirmation).</summary>
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
        [RoleCode.Low] = new(new[] { nameof(PalletReturn.LowExportNumber), nameof(PalletReturn.LowExportDate) }),
        [RoleCode.Hse] = new(new[] { nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate) }),
        [RoleCode.Viewer] = new(),
    };

    /// <summary>Owner edits everything; others need a Champion/Proxy grant for the pallet's VS in an area that owns the field.</summary>
    public static bool CanEdit(UserContext user, PalletReturn pallet, string field)
    {
        if (user.IsOwner) return true;
        if (pallet.Status.IsClosed() || pallet.IsLegacy) return false;
        return user.EditGrants.Any(g => g.ValueStreamId == pallet.ValueStreamId
                                        && Map.TryGetValue(g.Area, out var set) && set.Contains(field));
    }

    public static IReadOnlyCollection<string> EditableFields(RoleCode role) =>
        role == RoleCode.AdminQmmPe ? Fields.All.ToList() : Map.TryGetValue(role, out var set) ? set : Array.Empty<string>();
}
