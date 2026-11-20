using ExpoReturn.Domain;

namespace ExpoReturn.Application;

/// <summary>
/// Functional area that owns a block of fields (the Excel "Areas responsible to fill out information" row).
/// It comes from the user's department (QMM → Qmm, MFQ / PUQ → Requestor, OSD → Gs (customs), …).
/// "Requestor" is also the requesting side of any process: whoever requests it fills the request block.
/// AdminQmmPe and Viewer are kept for the Owner and read-only users.
/// </summary>
public enum RoleCode { AdminQmmPe, Qmm, Requestor, Gs, Lop, Mfo, Low, Hse, Viewer }

/// <summary>
/// What a user may do in a department and Value Stream (Owner ignores both):
/// Viewer reads (also while the registration is pending); Member captures the processes they create or request;
/// Proxy / Champion manage the processes of their department, edit their area's fields in every process of the VS,
/// also closed ones, and may delete.
/// </summary>
public enum UserFunction { Viewer = 0, Member = 1, Proxy = 2, Champion = 3, Owner = 4 }

public static class RoleInfo
{
    public static string DisplayName(this RoleCode r) => r switch
    {
        RoleCode.AdminQmmPe => "Owner (QMM / Process Excellence)",
        RoleCode.Qmm => "QMM",
        RoleCode.Requestor => "Requestor (MFQ / PUQ)",
        RoleCode.Gs => "Customs (OSD)",
        RoleCode.Lop => "Plant Logistics (LOP)",
        RoleCode.Mfo => "Manufacturing (MFO)",
        RoleCode.Low => "Warehouse (LOW)",
        RoleCode.Hse => "HSE",
        _ => "Viewer",
    };

    /// <summary>Short area name used in the access matrix.</summary>
    public static string ShortName(this RoleCode r) => r switch
    {
        RoleCode.Mfo => "MFO", RoleCode.Qmm => "QMM", RoleCode.Requestor => "MFQ", RoleCode.Gs => "OSD",
        RoleCode.Lop => "LOP", RoleCode.Low => "LOW", RoleCode.Hse => "HSE", RoleCode.AdminQmmPe => "Owner", _ => "Viewer",
    };

    /// <summary>Area of a department code (catalog Department.Area).</summary>
    public static RoleCode AreaOfDepartment(string? departmentCode)
    {
        return Catalogs.FindDepartment(departmentCode)?.Area switch
        {
            "QMM" => RoleCode.Qmm,
            "MFQ" or "PUQ" => RoleCode.Requestor,
            "OSD" or "GS" => RoleCode.Gs,
            "LOP" => RoleCode.Lop,
            "MFO" => RoleCode.Mfo,
            "LOW" => RoleCode.Low,
            "HSE" => RoleCode.Hse,
            _ => RoleCode.Viewer,
        };
    }

    public static bool CanCapture(this UserFunction f) => f >= UserFunction.Member;
    public static bool Manages(this UserFunction f) => f >= UserFunction.Proxy;
}

/// <summary>One row of the user access table: user × department × Value Stream × function.</summary>
public sealed record AccessGrant(string DepartmentCode, int ValueStreamId, UserFunction Function)
{
    public RoleCode Area => RoleInfo.AreaOfDepartment(DepartmentCode);
}

/// <summary>
/// The user running the application, read from Windows. Department and Value Streams come from the user's
/// profile; what they can do comes from the access grants (Member / Proxy / Champion) the Owner assigned.
/// </summary>
public sealed record UserContext(string NtUser, string DisplayName, string? DepartmentCode, bool IsOwner, IReadOnlyList<AccessGrant> Grants)
{
    /// <summary>Owner = full access (department and VS are irrelevant).</summary>
    public bool IsAdmin => IsOwner;
    /// <summary>Grants that allow capturing (Member, Proxy, Champion).</summary>
    public IEnumerable<AccessGrant> EditGrants => Grants.Where(g => g.Function.CanCapture());

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
    /// <summary>Heading in the "View as" list: the AREA of the department (which fields it edits), not the function.</summary>
    public string Group => IsOwner ? "Owner"
        : RoleInfo.AreaOfDepartment(DepartmentCode) is var area && area == RoleCode.Viewer
            ? "No department"
            : $"Area: {area.DisplayName()}";
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

    /// <summary>Fields of the IB PROCESS (header): request, customs entry and delivery number.</summary>
    public static readonly HashSet<string> Process = new()
    {
        nameof(IbProcess.QmmResponsible), nameof(IbProcess.RequestingDepartment), nameof(IbProcess.RequestorName),
        nameof(IbProcess.ReasonId), nameof(IbProcess.ReturnReasonDetail), nameof(IbProcess.ImportDate), nameof(IbProcess.ImportInvoice),
        nameof(IbProcess.ImportDateElpJup1), nameof(IbProcess.DeliveryNumber),
    };

    /// <summary>Registered users chosen with a field: same permission as the field they go with.</summary>
    public static readonly Dictionary<string, string> Linked = new()
    {
        [nameof(IbProcess.QmmResponsibleNtUser)] = nameof(IbProcess.QmmResponsible),
        [nameof(IbProcess.RequestorNtUser)] = nameof(IbProcess.RequestorName),
    };

    /// <summary>Fields of each pallet.</summary>
    public static IEnumerable<string> Pallet => All.Where(f => !Process.Contains(f));

    /// <summary>The fields of the request (QMM): header and pallet identity. Adding / removing pallets belongs here too.</summary>
    public static IReadOnlyCollection<string> RequestBlock => Stage1;
}

/// <summary>
/// Who may do what on an IB PROCESS (fields: IB_PROCESS_FIELD_PERMISSIONS).
/// <list type="bullet">
/// <item>Owner: everything, every process and VS.</item>
/// <item>The request is always made by QMM: QMM (Member, Proxy, Champion) creates IB processes in its VS and fills the request
/// (header and pallets). QMM Champion / Proxy: every process of the VS, delete too; QMM Member: the processes they created
/// or are QMM Responsible of.</item>
/// <item>OSD, LOP, MFO, LOW, HSE (Member, Proxy, Champion): only the fields of their area, in every process of their VS.</item>
/// <item>Every area of the form sets the Status Category (Excel COMMENTS) and writes comments; QMM Member sets it only in
/// its own processes (created or QMM Responsible), like the rest of the QMM fields. Comments: any QMM user of the VS.</item>
/// <item>Closed: only Owner, Champion and Proxy change them. Departments that are not part of the form (MFQ, PUQ) and
/// Viewers (or registration pending) only read.</item>
/// <item>Reading: every registered user sees every IB PROCESS of every VS (read only where they have no permission).</item>
/// </list>
/// </summary>
public static class FieldPermissions
{
    private static volatile Dictionary<RoleCode, HashSet<string>>? _fromDatabase;
    private static Dictionary<RoleCode, HashSet<string>> Map => _fromDatabase ?? BuiltIn;

    /// <summary>
    /// Takes the matrix read from IB_PROCESS_FIELD_PERMISSIONS (area → PalletReturn properties), so which area edits which
    /// field is managed in the database. An empty matrix is ignored.
    /// </summary>
    public static void UseMatrix(IReadOnlyDictionary<RoleCode, IReadOnlyCollection<string>> matrix)
    {
        if (matrix.Count == 0 || matrix.Values.All(v => v.Count == 0)) return;
        var map = matrix.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value));
        map.TryAdd(RoleCode.Viewer, new HashSet<string>());
        _fromDatabase = map;
    }

    /// <summary>Back to the built-in matrix (= 02_seed_data.sql; tests).</summary>
    public static void UseBuiltInMatrix() => _fromDatabase = null;

    /// <summary>Area code of IB_PROCESS_FIELD_PERMISSIONS.AREA_CODE.</summary>
    public static RoleCode? AreaOfCode(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "QMM" => RoleCode.Qmm, "REQUESTOR" => RoleCode.Requestor, "OSD" or "GS" => RoleCode.Gs, "LOP" => RoleCode.Lop,
        "MFO" => RoleCode.Mfo, "LOW" => RoleCode.Low, "HSE" => RoleCode.Hse, _ => null,
    };

    /// <summary>Status Category = Excel column COMMENTS: every area of the form sets it.</summary>
    private const string Category = nameof(PalletReturn.StatusCategoryCode);

    private static readonly Dictionary<RoleCode, HashSet<string>> BuiltIn = new()
    {
        [RoleCode.Qmm] = new(Fields.Stage1.Append(nameof(PalletReturn.StatusCategoryCode))),
        [RoleCode.Requestor] = new(),                            // MFQ / PUQ: read only (the request is made by QMM)
        [RoleCode.Gs] = new(new[]
        {
            nameof(PalletReturn.ImportDate), nameof(PalletReturn.ImportInvoice), nameof(PalletReturn.ImportDateElpJup1),
            nameof(PalletReturn.CustomsExportReference), Category,
        }),
        [RoleCode.Lop] = new(new[] { nameof(PalletReturn.DeliveryNumber), Category }),
        [RoleCode.Mfo] = new(Fields.Stage3.Append(nameof(PalletReturn.ScrapGrNumber)).Append(Category)),
        [RoleCode.Low] = new(new[] { nameof(PalletReturn.LowExportNumber), nameof(PalletReturn.LowExportDate), Category }),
        [RoleCode.Hse] = new(new[] { nameof(PalletReturn.HseScrapKg), nameof(PalletReturn.HseConfirmationDate), Category }),
        [RoleCode.Viewer] = new(),
    };

    /// <summary>Areas that fill the form (the requesting departments MFQ / PUQ and Viewer only read).</summary>
    public static bool IsFormArea(RoleCode area) =>
        area is RoleCode.Qmm or RoleCode.Gs or RoleCode.Lop or RoleCode.Mfo or RoleCode.Low or RoleCode.Hse;

    private static IEnumerable<AccessGrant> GrantsIn(UserContext user, IbProcess process) =>
        user.Grants.Where(g => g.ValueStreamId == process.ValueStreamId && g.Function.CanCapture() && IsFormArea(g.Area));

    /// <summary>The user created the process or is its QMM Responsible.</summary>
    private static bool IsMine(UserContext user, IbProcess process) =>
        NtUsers.Same(process.CreatedBy, user.NtUser) || NtUsers.Same(process.QmmResponsibleNtUser, user.NtUser);

    /// <summary>May the user change <paramref name="field"/> of the process (header field) or of <paramref name="pallet"/>?</summary>
    public static bool CanEdit(UserContext user, IbProcess process, PalletReturn? pallet, string field)
    {
        if (user.IsOwner) return true;
        field = Fields.Linked.GetValueOrDefault(field, field);
        var closed = pallet?.Status.IsClosed() ?? (process.Pallets.Count > 0 && process.Status.IsClosed());
        foreach (var g in GrantsIn(user, process))
        {
            if (closed && !g.Function.Manages()) continue;          // closed: Owner, Champion and Proxy only
            if (!Map.TryGetValue(g.Area, out var set) || !set.Contains(field)) continue;
            if (g.Area == RoleCode.Qmm && !g.Function.Manages() && !IsMine(user, process)) continue;   // QMM Member: own processes
            return true;
        }
        return false;
    }

    /// <summary>Same as before for code that still works pallet by pallet (its process is taken from the pallet).</summary>
    public static bool CanEdit(UserContext user, PalletReturn pallet, string field) =>
        CanEdit(user, pallet.Process, Fields.Process.Contains(field) ? null : pallet, field);

    /// <summary>Creates IB processes in the VS: QMM (Member, Proxy, Champion there) and the Owner.</summary>
    public static bool CanCreate(UserContext user, int valueStreamId) =>
        user.IsOwner || user.Grants.Any(g => g.ValueStreamId == valueStreamId && g.Function.CanCapture() && g.Area == RoleCode.Qmm);

    /// <summary>Deletes the process (and its pallets): Owner; QMM Champion / Proxy of the VS.</summary>
    public static bool CanDelete(UserContext user, IbProcess process) =>
        user.IsOwner || GrantsIn(user, process).Any(g => g.Area == RoleCode.Qmm && g.Function.Manages());

    /// <summary>Adds pallets: whoever fills the request of the process (QMM).</summary>
    public static bool CanAddPallet(UserContext user, IbProcess process) =>
        CanEdit(user, process, null, nameof(PalletReturn.ReturnedMaterial));

    /// <summary>A pallet not saved yet is removed by whoever added it; a saved one is deleted like the process.</summary>
    public static bool CanRemovePallet(UserContext user, IbProcess process, PalletReturn pallet) =>
        pallet.Id == 0 ? CanAddPallet(user, process) : CanDelete(user, process);

    /// <summary>Everybody reads every process of every VS (decision 2026-11-19); what they may change is decided field by field.</summary>
    public static bool CanRead(UserContext user, IbProcess process) => true;

    /// <summary>Writes comments: Owner and the users (Member, Proxy, Champion) of the six areas of the form in the VS.</summary>
    public static bool CanComment(UserContext user, IbProcess process) => user.IsOwner || GrantsIn(user, process).Any();

    public static IReadOnlyCollection<string> EditableFields(RoleCode role) =>
        role == RoleCode.AdminQmmPe ? Fields.All.ToList() : Map.TryGetValue(role, out var set) ? set : Array.Empty<string>();
}

/// <summary>Person name from the Windows display name ("EXTERNAL Reyna Morisco Luis David (PM, ME/QMM-L2-JuP1)").</summary>
public static class PersonName
{
    /// <summary>
    /// Leaves only the name: without the "EXTERNAL" / "EXTERN" mark in front and without the part in parentheses at the end
    /// (company / department). Returns null when nothing is left or when it is only the account name.
    /// </summary>
    public static string? FromDisplayName(string? displayName, string? accountName = null)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        var s = displayName.Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^(EXTERNAL|EXTERN|EXT\.?)\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\([^()]*\)\s*$", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length == 0) return null;
        if (accountName is not null && string.Equals(s, accountName.Split('\\').Last(), StringComparison.OrdinalIgnoreCase)) return null;
        return s;
    }
}

/// <summary>
/// Windows user as the application stores it: only the account, without the domain ("US\\RLN8JU" → "RLN8JU"), in capitals.
/// Older values with domain still compare equal (<see cref="Same"/>).
/// </summary>
public static class NtUsers
{
    public static string Normalize(string? ntUser)
    {
        if (string.IsNullOrWhiteSpace(ntUser)) return "";
        var s = ntUser.Trim();
        var i = s.LastIndexOf('\\');
        return (i >= 0 ? s[(i + 1)..] : s).Trim().ToUpperInvariant();
    }

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Normalize(a) == Normalize(b);
}
