using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>"Pending for my area" (agreed 2026-11-19): which area has something to capture in an open pallet.</summary>
public class PendingWorkTests
{
    private static readonly DateTime Today = DateTime.Today;

    private static (IbProcess Process, PalletReturn Pallet) Requested(Action<IbProcess, PalletReturn>? change = null)
    {
        var p = new IbProcess
        {
            ValueStreamId = 7, CreatedBy = @"BOSCH\QMMA", QmmResponsible = "Qmm A", RequestingDepartment = "MFQ",
            RequestorName = "Req", ReasonId = 4,
        };
        var x = p.NewPallet(@"BOSCH\QMMA");
        x.ReturnedMaterial = "0285.500.081-2ZD"; x.QtyImported = 100; x.OriginalHU = "2102587877"; x.ReturnPONumber = "12345678";
        change?.Invoke(p, x);
        PalletEvaluator.Apply(p, CalcParameters.Default, Today);
        return (p, x);
    }

    private static void Crossed(IbProcess p, PalletReturn _) => p.ImportDateElpJup1 = Today.AddDays(-20);

    private static RoleCode[] Pending(PalletReturn x) =>
        new[] { RoleCode.Qmm, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse }.Where(a => PendingWork.IsPendingFor(a, x)).ToArray();

    [Fact]
    public void Draft_is_pending_for_qmm_only()
    {
        var (_, x) = Requested((p, _) => p.ReasonId = null);
        Assert.Equal(WorkflowStatus.Draft, x.Status);
        Assert.Equal(new[] { RoleCode.Qmm }, Pending(x));
    }

    [Fact]
    public void Requested_without_crossing_is_pending_for_osd()
    {
        var (_, x) = Requested();
        Assert.Equal(WorkflowStatus.Requested, x.Status);
        Assert.Equal(new[] { RoleCode.Gs }, Pending(x));
    }

    [Fact]
    public void After_the_crossing_lop_needs_the_delivery_and_mfo_has_the_pieces_on_its_floor()
    {
        var (_, x) = Requested(Crossed);
        Assert.Equal(new[] { RoleCode.Lop, RoleCode.Mfo }, Pending(x));
        var (_, y) = Requested((p, a) => { Crossed(p, a); p.DeliveryNumber = "8000000001"; });
        Assert.Equal(new[] { RoleCode.Mfo }, Pending(y));
    }

    [Fact]
    public void Old_material_goes_to_low_then_customs_export_to_osd()
    {
        var (_, toLow) = Requested((p, a) => { Crossed(p, a); p.DeliveryNumber = "8000000001"; a.OldMaterialDeliveredQty = 100; a.PhysicalDeliveredQty = 0; });
        Assert.Equal(new[] { RoleCode.Low }, Pending(toLow));
        var (_, toOsd) = Requested((p, a) =>
        {
            Crossed(p, a); p.DeliveryNumber = "8000000001"; a.OldMaterialDeliveredQty = 100; a.PhysicalDeliveredQty = 0;
            a.LowExportNumber = 10; a.LowExportDate = Today.AddDays(-2);
        });
        Assert.Equal(new[] { RoleCode.Gs }, Pending(toOsd));
    }

    [Fact]
    public void Scrap_needs_the_gr_from_mfo_and_the_confirmation_from_hse()
    {
        var (_, x) = Requested((p, a) =>
        {
            Crossed(p, a); p.DeliveryNumber = "8000000001"; a.OldMaterialDeliveredQty = 0; a.ScrapQty = 100; a.ScrapDate = Today.AddDays(-5);
            a.PhysicalDeliveredQty = 0;
        });
        Assert.Equal(new[] { RoleCode.Mfo, RoleCode.Hse }, Pending(x));
        var (_, y) = Requested((p, a) =>
        {
            Crossed(p, a); p.DeliveryNumber = "8000000001"; a.ScrapQty = 100; a.ScrapDate = Today.AddDays(-5); a.PhysicalDeliveredQty = 0;
            a.ScrapGrNumber = "5000000001";
        });
        Assert.Equal(new[] { RoleCode.Hse }, Pending(y));
    }

    [Fact]
    public void A_closed_pallet_is_pending_for_nobody()
    {
        var (_, x) = Requested((p, a) =>
        {
            Crossed(p, a); a.ScrapQty = 100; a.ScrapDate = Today.AddDays(-5); a.PhysicalDeliveredQty = 0; a.ScrapGrNumber = "5000000001";
            a.HseScrapKg = 2m; a.HseConfirmationDate = Today.AddDays(-1);
        });
        Assert.True(x.Status.IsClosed());
        Assert.Empty(Pending(x));                       // even without Delivery Number: LOP has nothing left when the pallet is closed
    }

    [Fact]
    public void Scopes_follow_the_access_and_a_qmm_member_only_sees_their_own_processes()
    {
        var dir = new UserDirectory((string?)null, new[] { @"BOSCH\OWNER" }, includeDemoUsers: false);
        dir.ReplaceAccess(new[]
        {
            new UserAccess(@"BOSCH\QMMA", "QMM", 7, UserFunction.Member), new UserAccess(@"BOSCH\QMMB", "QMM", 7, UserFunction.Member),
            new UserAccess(@"BOSCH\QMMC", "QMM", 7, UserFunction.Champion), new UserAccess(@"BOSCH\OSD1", "OSD", 1, UserFunction.Member),
            new UserAccess(@"BOSCH\MFQ7", "MFQ", 7, UserFunction.Champion),
        });
        var (draft, _) = Requested((p, _) => p.ReasonId = null);
        bool Sees(string nt) => PendingWork.Matches(draft, PendingWork.ScopesFor(dir.ContextFor(nt)));
        Assert.True(Sees(@"BOSCH\QMMA"));               // created it
        Assert.False(Sees(@"BOSCH\QMMB"));              // another QMM member
        Assert.True(Sees(@"BOSCH\QMMC"));               // QMM Champion: whole VS
        Assert.True(Sees(@"BOSCH\OWNER"));              // Owner: every area and VS
        Assert.Empty(PendingWork.ScopesFor(dir.ContextFor(@"BOSCH\MFQ7")));   // MFQ / PUQ do not fill the form

        var (requested, _) = Requested();
        Assert.False(PendingWork.Matches(requested, PendingWork.ScopesFor(dir.ContextFor(@"BOSCH\OSD1"))));   // OSD of VS1, process in VS7
        requested.ValueStreamId = 1;
        Assert.True(PendingWork.Matches(requested, PendingWork.ScopesFor(dir.ContextFor(@"BOSCH\OSD1"))));
    }
}
