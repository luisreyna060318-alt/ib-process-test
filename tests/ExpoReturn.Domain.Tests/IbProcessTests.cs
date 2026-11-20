using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>v0.5.0: an IB PROCESS (header) with several pallets, permissions by function / department / area, delete.</summary>
public class IbProcessTests
{
    private static UserContext U(string nt, string dept, UserFunction f, int vs = 4) =>
        new(nt, nt, dept, false, new[] { new AccessGrant(dept, vs, f) });

    private static readonly UserContext Owner = new("BOSCH\\OWNER", "Owner", null, true, Array.Empty<AccessGrant>());
    private static readonly UserContext QmmChampion = U("B\\QMMC", "QMM", UserFunction.Champion);
    private static readonly UserContext MfqChampion = U("B\\MFQC", "MFQ", UserFunction.Champion);
    private static readonly UserContext PuqChampion = U("B\\PUQC", "PUQ", UserFunction.Champion);
    private static readonly UserContext Creator = U("B\\QMMM", "QMM", UserFunction.Member);          // QMM member who created it
    private static readonly UserContext OtherMember = U("B\\QMM2", "QMM", UserFunction.Member);
    private static readonly UserContext MfoMember = U("B\\MFOM", "MFO", UserFunction.Member);
    private static readonly UserContext MfoMemberVs1 = U("B\\MFO1", "MFO", UserFunction.Member, vs: 1);
    private static readonly UserContext Viewer = U("B\\VIEW", "MFQ", UserFunction.Viewer);

    private static PalletReturn Pallet(string hu, string po = "49398082") => new()
    {
        ReturnedMaterial = "0203.306.644-250", QtyImported = 10, OriginalHU = hu, ReturnPONumber = po,
        DateReturnedToWhs = new DateTime(2026, 10, 1), CreatedBy = Creator.NtUser,
    };

    private static IbProcess Process(int pallets = 2)
    {
        var p = new IbProcess
        {
            ValueStreamId = 4, CreatedBy = Creator.NtUser, RequestingDepartment = "MFQ", RequestorName = "MFQ C", RequestorNtUser = MfqChampion.NtUser,
            QmmResponsible = "QMM C", QmmResponsibleNtUser = QmmChampion.NtUser, ReasonId = 6,
        };
        for (var i = 0; i < pallets; i++) p.AddPallet(Pallet($"210489184{i}"));
        return p;
    }

    private static (InMemoryProcessRepository Repo, InMemoryAuditTrail Audit, ProcessService Svc) Service()
    {
        var repo = new InMemoryProcessRepository();
        var audit = new InMemoryAuditTrail();
        return (repo, audit, new ProcessService(repo, audit, new SystemClock()));
    }

    private static IbProcess Saved(ProcessService svc, int pallets = 2)
    {
        var r = svc.Save(Process(pallets), Owner, "test");
        Assert.True(r.Success, r.Summary);
        return r.Saved!;
    }

    // ------------------------------------------------------------------ permissions

    [Fact]
    public void Owner_does_everything()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanEdit(Owner, p, null, nameof(IbProcess.ReasonId)));
        Assert.True(FieldPermissions.CanEdit(Owner, p, p.Pallets[0], nameof(PalletReturn.HseScrapKg)));
        Assert.True(FieldPermissions.CanDelete(Owner, p));
        Assert.True(FieldPermissions.CanCreate(Owner, 9));
    }

    [Fact]
    public void Qmm_champion_creates_fills_every_request_of_the_vs_and_deletes()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanCreate(QmmChampion, 4));
        Assert.True(FieldPermissions.CanEdit(QmmChampion, p, null, nameof(IbProcess.ReasonId)));
        Assert.True(FieldPermissions.CanEdit(QmmChampion, p, p.Pallets[1], nameof(PalletReturn.ReturnPONumber)));
        Assert.True(FieldPermissions.CanAddPallet(QmmChampion, p));
        Assert.True(FieldPermissions.CanDelete(QmmChampion, p));
        Assert.False(FieldPermissions.CanEdit(QmmChampion, p, p.Pallets[0], nameof(PalletReturn.NewHU)));     // MFO field
        Assert.False(FieldPermissions.CanCreate(QmmChampion, 1));                                            // another VS
    }

    [Fact]
    public void Requesting_departments_are_not_part_of_the_form_and_only_read()
    {
        var p = Process();
        foreach (var u in new[] { MfqChampion, PuqChampion, U("B\\MFQ9", "MFQ", UserFunction.Member) })
        {
            Assert.False(FieldPermissions.CanCreate(u, 4));
            Assert.DoesNotContain(Fields.All, f => FieldPermissions.CanEdit(u, p, Fields.Process.Contains(f) ? null : p.Pallets[0], f));
            Assert.False(FieldPermissions.CanDelete(u, p));
            Assert.False(FieldPermissions.CanComment(u, p));
            Assert.True(FieldPermissions.CanRead(u, p));
        }
        Assert.False(FieldPermissions.CanEdit(MfqChampion, p, null, nameof(IbProcess.RequestorNtUser)));    // even when it is the requestor
    }

    [Fact]
    public void Qmm_member_fills_the_requests_they_created_or_are_responsible_for_and_never_deletes()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanCreate(Creator, 4));
        Assert.True(FieldPermissions.CanEdit(Creator, p, null, nameof(IbProcess.ReasonId)));
        Assert.True(FieldPermissions.CanAddPallet(Creator, p));
        Assert.False(FieldPermissions.CanDelete(Creator, p));
        Assert.False(FieldPermissions.CanRemovePallet(Creator, p, p.Pallets[0] is var x && (x.Id = 5) > 0 ? x : x));   // saved pallet
        Assert.False(FieldPermissions.CanEdit(OtherMember, p, null, nameof(IbProcess.ReasonId)));
        p.QmmResponsibleNtUser = OtherMember.NtUser;                                                          // now responsible
        Assert.True(FieldPermissions.CanEdit(OtherMember, p, null, nameof(IbProcess.ReasonId)));
        Assert.False(FieldPermissions.CanEdit(Creator, p, p.Pallets[0], nameof(PalletReturn.ScrapQty)));
    }

    [Fact]
    public void Every_area_of_the_form_sets_the_status_category_and_writes_comments()
    {
        var p = Process();
        foreach (var dept in new[] { "QMM", "OSD", "LOP", "MFO", "LOW", "HSE" })
        {
            var u = U($"B\\{dept}P", dept, UserFunction.Proxy);
            Assert.True(FieldPermissions.CanEdit(u, p, p.Pallets[0], nameof(PalletReturn.StatusCategoryCode)), dept);
            Assert.True(FieldPermissions.CanComment(u, p), dept);
        }
        Assert.False(FieldPermissions.CanComment(Viewer, p));
        Assert.True(FieldPermissions.CanComment(Owner, p));
    }

    [Fact]
    public void Support_area_members_capture_their_fields_on_any_process_of_their_vs()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanEdit(MfoMember, p, p.Pallets[0], nameof(PalletReturn.ScrapQty)));
        Assert.False(FieldPermissions.CanEdit(MfoMember, p, null, nameof(IbProcess.ReasonId)));
        Assert.False(FieldPermissions.CanAddPallet(MfoMember, p));
        Assert.False(FieldPermissions.CanCreate(MfoMember, 4));
        Assert.False(FieldPermissions.CanEdit(MfoMemberVs1, p, p.Pallets[0], nameof(PalletReturn.ScrapQty)));
        Assert.True(FieldPermissions.CanEdit(U("B\\OSD", "OSD", UserFunction.Member), p, null, nameof(IbProcess.ImportInvoice)));
        Assert.True(FieldPermissions.CanEdit(U("B\\LOP", "LOP", UserFunction.Member), p, null, nameof(IbProcess.DeliveryNumber)));
    }

    [Fact]
    public void Viewer_only_reads_and_closed_pallets_are_for_owner_champion_and_proxy()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanRead(Viewer, p));
        Assert.False(FieldPermissions.CanCreate(Viewer, 4));
        Assert.DoesNotContain(Fields.All, f => FieldPermissions.CanEdit(Viewer, p, Fields.Process.Contains(f) ? null : p.Pallets[0], f));

        var closed = p.Pallets[0];
        closed.Status = WorkflowStatus.Exported;
        Assert.False(FieldPermissions.CanEdit(MfoMember, p, closed, nameof(PalletReturn.ScrapQty)));
        Assert.True(FieldPermissions.CanEdit(U("B\\MFOP", "MFO", UserFunction.Proxy), p, closed, nameof(PalletReturn.ScrapQty)));
    }

    [Fact]
    public void Linked_user_fields_follow_the_permission_of_their_field()
    {
        var p = Process();
        Assert.True(FieldPermissions.CanEdit(Creator, p, null, nameof(IbProcess.RequestorNtUser)));
        Assert.False(FieldPermissions.CanEdit(MfoMember, p, null, nameof(IbProcess.QmmResponsibleNtUser)));
    }

    // ------------------------------------------------------------------ creation and pallets

    [Fact]
    public void Whoever_creates_is_the_qmm_responsible_and_qmm_fills_department_and_requestor()
    {
        var (_, _, svc) = Service();
        var byQmm = svc.NewDraft(4, QmmChampion);
        Assert.Equal(QmmChampion.NtUser, byQmm.QmmResponsibleNtUser);
        Assert.Null(byQmm.RequestorNtUser);
        Assert.Null(byQmm.RequestingDepartment);
        Assert.Single(byQmm.Pallets);

        var byOwner = svc.NewDraft(4, Owner);
        Assert.Null(byOwner.QmmResponsibleNtUser);
        Assert.Null(byOwner.RequestorNtUser);
    }

    [Fact]
    public void Only_qmm_or_the_owner_can_save_a_new_process()
    {
        var (_, _, svc) = Service();
        var refused = svc.Save(Process(1), MfqChampion);
        Assert.False(refused.Success);
        Assert.Contains("create IB processes in VS4", refused.DeniedFields);
        Assert.True(svc.Save(Process(1), Creator).Success);
    }

    [Fact]
    public void Comments_need_a_saved_process_an_area_of_the_form_and_text_and_go_into_the_copy_of_a_deleted_process()
    {
        var repo = new InMemoryProcessRepository();
        var audit = new InMemoryAuditTrail();
        var svc = new ProcessService(repo, audit, new SystemClock(), null, new InMemoryProcessComments());
        Assert.False(svc.AddComment(Process(1), MfoMember, "x").Success);                  // not saved yet
        var saved = Saved(svc, 1);
        Assert.False(svc.AddComment(saved, MfqChampion, "x").Success);                   // read only
        Assert.False(svc.AddComment(saved, MfoMember, "  ").Success);
        Assert.False(svc.AddComment(saved, MfoMember, new string('x', 1001)).Success);
        Assert.True(svc.AddComment(saved, MfoMember, " Waiting for the GR number ").Success);
        var c = Assert.Single(svc.Comments(saved));
        Assert.Equal("Waiting for the GR number", c.Text);
        Assert.Equal("MFO", c.DepartmentCode);
        Assert.Equal("B\\MFOM · MFO", c.Author);

        Assert.True(svc.Delete(saved, QmmChampion, "Created by mistake").Success);
        Assert.Contains("Waiting for the GR number", Assert.Single(audit.For(saved.Id), a => a.Deleted).Snapshot);
    }

    [Fact]
    public void A_new_pallet_takes_the_po_and_po_date_of_the_previous_one()
    {
        var p = Process(1);
        var second = p.NewPallet("B\\X");
        Assert.Equal(2, second.LineNo);
        Assert.Equal("49398082", second.ReturnPONumber);
        Assert.Equal(new DateTime(2026, 10, 1), second.DateReturnedToWhs);
        Assert.Null(second.OriginalHU);
        Assert.Equal($"{p.FolioCode}-02", second.FolioCode);
    }

    [Fact]
    public void Every_pallet_needs_po_and_hu_or_batch_and_cannot_repeat_but_the_po_can()
    {
        var p = Process();
        Assert.DoesNotContain(PalletValidator.ValidateProcess(p, DateTime.Today), i => i.Severity == Severity.Error);   // same PO twice: fine

        p.Pallets[1].OriginalHU = p.Pallets[0].OriginalHU;
        var repeated = Assert.Single(PalletValidator.ValidateProcess(p, DateTime.Today), i => i.Code == "VAL-053");
        Assert.Equal(Severity.Error, repeated.Severity);
        Assert.Equal(2, repeated.Pallet);
        Assert.StartsWith("Pallet 2: Pallet 2104891840 is repeated in pallets 1, 2", repeated.Text);

        p.Pallets[1].OriginalHU = null;
        p.Pallets[1].OriginalBatch = "AA23743599";                           // the batch is a valid identifier
        Assert.DoesNotContain(PalletValidator.ValidateProcess(p, DateTime.Today), i => i.Severity == Severity.Error);

        p.Pallets[1].OriginalBatch = null;
        p.Pallets[1].ReturnPONumber = null;
        var issues = PalletValidator.ValidateProcess(p, DateTime.Today);
        Assert.Contains(issues, i => i.Code == "VAL-050" && i.Pallet == 2);
        Assert.Contains(issues, i => i.Code == "VAL-051" && i.Pallet == 2);
        Assert.StartsWith("Pallet 2: ", issues.First(i => i.Code == "VAL-051").Text);
    }

    [Fact]
    public void A_po_already_used_in_another_process_is_reported_before_saving()
    {
        var (_, _, svc) = Service();
        var first = Saved(svc);
        var second = Process(1);
        Assert.Equal(first.FolioCode, Assert.Single(Assert.Single(svc.PoInOtherProcesses(second)).Folios));
        second.Pallets[0].ReturnPONumber = "41111111";
        Assert.Empty(svc.PoInOtherProcesses(second));
        Assert.Empty(svc.PoInOtherProcesses(first.Clone()));                // its own process does not count
    }

    // ------------------------------------------------------------------ status of the process

    [Theory]
    [InlineData(new WorkflowStatus[0], WorkflowStatus.Draft)]
    [InlineData(new[] { WorkflowStatus.Imported, WorkflowStatus.DutyRisk, WorkflowStatus.Exported }, WorkflowStatus.DutyRisk)]
    [InlineData(new[] { WorkflowStatus.PendingExport, WorkflowStatus.Imported, WorkflowStatus.Exported }, WorkflowStatus.Imported)]
    [InlineData(new[] { WorkflowStatus.Exported, WorkflowStatus.ClosedDutyPaid }, WorkflowStatus.ClosedDutyPaid)]
    [InlineData(new[] { WorkflowStatus.Exported, WorkflowStatus.Scrapped }, WorkflowStatus.Exported)]
    [InlineData(new[] { WorkflowStatus.Scrapped }, WorkflowStatus.Scrapped)]
    public void Process_status_comes_from_its_pallets(WorkflowStatus[] pallets, WorkflowStatus expected) =>
        Assert.Equal(expected, IbProcess.StatusOf(pallets));

    [Fact]
    public void Process_totals_add_up_the_pallets()
    {
        var p = Process(3);
        Assert.Equal(3, p.PalletCount);
        Assert.Equal(30, p.QtyImported);
        Assert.Equal("0203.306.644-250", p.PartNumbersText);
        Assert.Equal("49398082", p.PoNumbersText);
    }

    // ------------------------------------------------------------------ save and delete

    [Fact]
    public void Saving_assigns_one_folio_to_the_process_and_numbers_the_pallets()
    {
        var (repo, _, svc) = Service();
        var p = Saved(svc, 3);
        Assert.True(p.FolioNumber > 0);
        Assert.Equal(new[] { 1, 2, 3 }, p.Pallets.Select(x => x.LineNo));
        Assert.All(p.Pallets, x => Assert.True(x.Id > 0));
        Assert.Equal(3, repo.AllPallets().Count);
        Assert.Equal($"{p.FolioCode}-03", p.Pallets[2].FolioCode);
    }

    [Fact]
    public void A_member_cannot_remove_a_saved_pallet_but_the_owner_can_and_the_audit_keeps_a_copy()
    {
        var (repo, audit, svc) = Service();
        var saved = Saved(svc, 2);

        var byMember = repo.Get(saved.Id)!.Clone();
        byMember.RemovePallet(byMember.Pallets[1]);
        var refused = svc.Save(byMember, Creator, "no longer needed");
        Assert.False(refused.Success);
        Assert.Contains("delete pallet 2", refused.DeniedFields);

        var byOwner = repo.Get(saved.Id)!.Clone();
        var removedId = byOwner.Pallets[1].Id;
        byOwner.RemovePallet(byOwner.Pallets[1]);
        Assert.True(svc.Save(byOwner, Owner, "no longer needed").Success);
        Assert.Single(repo.Get(saved.Id)!.Pallets);
        Assert.Null(repo.GetPallet(removedId));
        var entry = Assert.Single(audit.For(saved.Id), a => a.Deleted);
        Assert.Contains("0203.306.644-250", entry.Snapshot);
    }

    [Fact]
    public void Deleting_a_process_needs_permission_and_a_reason_and_leaves_a_snapshot()
    {
        var (repo, audit, svc) = Service();
        var saved = Saved(svc, 2);
        Assert.False(svc.Delete(saved, MfqChampion, "x").Success);                      // requesting department: read only
        Assert.False(svc.Delete(saved, Creator, "x").Success);                          // QMM member: no delete
        Assert.False(svc.Delete(saved, QmmChampion, " ").Success);
        Assert.NotNull(repo.Get(saved.Id));

        Assert.True(svc.Delete(saved, QmmChampion, "Created by mistake").Success);
        Assert.Null(repo.Get(saved.Id));
        Assert.Empty(repo.AllPallets());
        var entry = Assert.Single(audit.For(saved.Id), a => a.Deleted && a.Field == "IB PROCESS");
        Assert.Equal("Created by mistake", entry.Reason);
        Assert.Contains(saved.FolioCode, entry.Snapshot);
        Assert.Contains("2104891841", entry.Snapshot);                       // the pallets are in the copy too
    }

    [Fact]
    public void A_header_change_is_audited_once_for_the_process()
    {
        var (repo, audit, svc) = Service();
        var saved = Saved(svc, 3);
        var edit = repo.Get(saved.Id)!.Clone();
        edit.ImportInvoice = "MPS26000222";
        Assert.True(svc.Save(edit, U("B\\OSD", "OSD", UserFunction.Proxy), null).Success);
        Assert.Single(audit.For(saved.Id), a => a.Field == nameof(IbProcess.ImportInvoice));
    }

    // ------------------------------------------------------------------ registration

    [Fact]
    public void Registration_waits_as_viewer_and_approval_makes_the_user_member()
    {
        var dir = new UserDirectory((string?)null, new[] { "BOSCH\\OWNER" }, includeDemoUsers: false);
        dir.Register("BOSCH\\NEW1", "New One", "LOP", new[] { 4, 7 });
        Assert.All(dir.ContextFor("BOSCH\\NEW1").Grants, g => Assert.Equal(UserFunction.Viewer, g.Function));
        var req = Assert.Single(dir.Requests(), r => r.NtUser == "NEW1");
        Assert.True(req.IsRegistration);
        Assert.Equal("New user", req.TypeText);

        dir.Decide(req.Id, approve: true, "BOSCH\\OWNER", null);
        var u = dir.ContextFor("BOSCH\\NEW1");
        Assert.Equal(new[] { 4, 7 }, u.Grants.Select(g => g.ValueStreamId).OrderBy(v => v));
        Assert.All(u.Grants, g => Assert.Equal(UserFunction.Member, g.Function));
        Assert.Empty(dir.Requests());
    }
}
