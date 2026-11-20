using ExpoReturn.Domain;

namespace ExpoReturn.Application;

/// <summary>
/// One area of the form in one Value Stream where the user captures ("Pending for my area").
/// <paramref name="ValueStreamId"/> 0 = every VS (Owner). <paramref name="OnlyOwnOf"/>: QMM Member, only the processes
/// that user created or is QMM Responsible of (same rule as the QMM fields).
/// </summary>
public sealed record AreaScope(RoleCode Area, int ValueStreamId, string? OnlyOwnOf = null);

/// <summary>
/// "Pending for my area" (agreed with the process owner on 2026-11-19): the processes of the user's VS with at least one
/// OPEN pallet in which that area has something to capture. Duty risk is not a pending of its own (it stays in Alerts).
/// <list type="bullet">
/// <item>QMM: pallet in Draft (request incomplete).</item>
/// <item>OSD: no El Paso → JuP1 crossing yet (Requested), or LOW already exported and the Customs Export is missing.</item>
/// <item>LOP: crossing captured and no Delivery Number.</item>
/// <item>MFO: pieces on its floor (crossing captured, no physical delivered qty, pieces left) or scrap without GR number.</item>
/// <item>LOW: old material delivered and LOW export number or date missing.</item>
/// <item>HSE: scrap without HSE confirmation (date and kg).</item>
/// </list>
/// The SQL of the inbox (OracleProcessQueries) follows exactly the same conditions.
/// </summary>
public static class PendingWork
{
    /// <summary>Areas of the form where the user captures, by VS. Owner: every area in every VS. MFQ / PUQ / Viewer: none.</summary>
    public static IReadOnlyList<AreaScope> ScopesFor(UserContext user)
    {
        if (user.IsOwner)
            return new[] { RoleCode.Qmm, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse }
                .Select(a => new AreaScope(a, 0)).ToList();
        return user.EditGrants.Where(g => FieldPermissions.IsFormArea(g.Area))
            .Select(g => new AreaScope(g.Area, g.ValueStreamId, g.Area == RoleCode.Qmm && !g.Function.Manages() ? user.NtUser : null))
            .Distinct().ToList();
    }

    public static bool IsPendingFor(RoleCode area, PalletReturn p)
    {
        if (p.Status.IsClosed()) return false;
        var old = p.OldMaterialDeliveredQty ?? 0;
        var scrap = p.ScrapQty ?? 0;
        var crossed = p.Process.ImportDateElpJup1 is not null;
        static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
        return area switch
        {
            RoleCode.Qmm => p.Status == WorkflowStatus.Draft,
            RoleCode.Gs => p.Status == WorkflowStatus.Requested || (old > 0 && p.LowExportNumber is not null && Blank(p.CustomsExportReference)),
            RoleCode.Lop => crossed && Blank(p.Process.DeliveryNumber),
            RoleCode.Mfo => (crossed && p.PhysicalDeliveredQty is null && p.QtyImported - old - scrap > 0) || (scrap > 0 && Blank(p.ScrapGrNumber)),
            RoleCode.Low => old > 0 && (p.LowExportNumber is null || p.LowExportDate is null),
            RoleCode.Hse => scrap > 0 && (p.HseConfirmationDate is null || p.HseScrapKg is null or <= 0),
            _ => false,
        };
    }

    /// <summary>The process has an open pallet pending for one of the scopes.</summary>
    public static bool Matches(IbProcess process, IReadOnlyList<AreaScope> scopes) =>
        scopes.Any(s =>
            (s.ValueStreamId == 0 || s.ValueStreamId == process.ValueStreamId)
            && (s.OnlyOwnOf is null
                || NtUsers.Same(process.CreatedBy, s.OnlyOwnOf)
                || NtUsers.Same(process.QmmResponsibleNtUser, s.OnlyOwnOf))
            && process.Pallets.Any(p => IsPendingFor(s.Area, p)));

    /// <summary>SQL condition of an area over IB_PROCESS_PALLETS_V v, IB_PROCESS_PALLETS x and IB_PROCESS_PROCESSES m.</summary>
    public static string SqlCondition(RoleCode area) => area switch
    {
        RoleCode.Qmm => "v.WORKFLOW_STATUS_ID = 1",
        RoleCode.Gs => "(v.WORKFLOW_STATUS_ID = 2 OR (NVL(x.OLD_MATERIAL_DELIVERED_QTY, 0) > 0 AND x.LOW_EXPORT_NUMBER IS NOT NULL AND TRIM(x.CUSTOMS_EXPORT) IS NULL))",
        RoleCode.Lop => "(m.ELP_JUP1_CROSSING_DATE IS NOT NULL AND TRIM(m.DELIVERY_NUMBER) IS NULL)",
        RoleCode.Mfo => "((m.ELP_JUP1_CROSSING_DATE IS NOT NULL AND x.PHYSICAL_DELIVERED_QTY IS NULL" +
                        " AND x.QTY_IMPORTED - NVL(x.OLD_MATERIAL_DELIVERED_QTY, 0) - NVL(x.SCRAP_QTY, 0) > 0)" +
                        " OR (NVL(x.SCRAP_QTY, 0) > 0 AND TRIM(x.GR_NUMBER) IS NULL))",
        RoleCode.Low => "(NVL(x.OLD_MATERIAL_DELIVERED_QTY, 0) > 0 AND (x.LOW_EXPORT_NUMBER IS NULL OR x.LOW_EXPORT_DATE IS NULL))",
        RoleCode.Hse => "(NVL(x.SCRAP_QTY, 0) > 0 AND (x.HSE_CONFIRMATION_DATE IS NULL OR NVL(x.HSE_SCRAP_KG, 0) <= 0))",
        _ => "1 = 0",
    };
}
