using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>Users identified by Windows account; access = department × VS × function; Owner assigns.</summary>
public class UserAccessTests
{
    private static PalletReturn Imported(int vs) => new()
    {
        ValueStreamId = vs, Status = WorkflowStatus.Imported, ReturnedMaterial = "0203.306.644-250", QtyImported = 10,
    };

    [Fact]
    public void First_run_registers_the_user_as_viewer_in_the_selected_value_streams()
    {
        var dir = new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: false);
        dir.Register("bosch\\abc1jup", "Ana Uno", "OSD", new[] { 4, 7 });

        var user = dir.ContextFor("BOSCH\\ABC1JUP");
        Assert.Equal(UserFunction.Viewer, user.Function);
        Assert.Equal(new[] { 4, 7 }, user.ValueStreams);
        Assert.False(FieldPermissions.CanEdit(user, Imported(4), nameof(PalletReturn.ImportInvoice)));   // Viewer: read-only
    }

    [Fact]
    public void Champion_or_proxy_edits_only_their_area_fields_in_their_value_streams()
    {
        var dir = new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: false);
        dir.Register("BOSCH\\GS1", "Gus Dos", "OSD", new[] { 4 });
        dir.ReplaceAccess(new[] { new UserAccess("BOSCH\\GS1", "OSD", 4, UserFunction.Proxy) });

        var gs = dir.ContextFor("BOSCH\\GS1");
        Assert.True(FieldPermissions.CanEdit(gs, Imported(4), nameof(PalletReturn.ImportInvoice)));
        Assert.False(FieldPermissions.CanEdit(gs, Imported(1), nameof(PalletReturn.ImportInvoice)));      // other VS
        Assert.False(FieldPermissions.CanEdit(gs, Imported(4), nameof(PalletReturn.ScrapQty)));           // MFO field
    }

    [Fact]
    public void Everybody_reads_every_value_stream_but_edits_only_where_they_have_permission()
    {
        // Decision 2026-11-19: every registered user sees every IB PROCESS of every VS, read only where they have no permission.
        var dir = new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: false);
        dir.Register("BOSCH\\QMM1", "Qmm Uno", "QMM", new[] { 1 });
        dir.ReplaceAccess(new[] { new UserAccess("BOSCH\\QMM1", "QMM", 1, UserFunction.Champion) });
        var qmmVs1 = dir.ContextFor("BOSCH\\QMM1");
        dir.Register("BOSCH\\NEW1", "Nuevo Uno", "MFQ", new[] { 4 });
        var viewer = dir.ContextFor("BOSCH\\NEW1");

        var vs7 = Imported(7);
        Assert.True(FieldPermissions.CanRead(qmmVs1, vs7.Process));
        Assert.True(FieldPermissions.CanRead(viewer, vs7.Process));
        Assert.False(FieldPermissions.CanEdit(qmmVs1, vs7, nameof(PalletReturn.ReturnReasonDetail)));
        Assert.False(FieldPermissions.CanComment(qmmVs1, vs7.Process));
    }

    [Fact]
    public void Qmm_member_sets_the_status_category_only_in_their_own_processes()
    {
        // Decision 2026-11-19: "por algo es el QMM Responsible". The other areas set it in every process of their VS.
        var dir = new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: false);
        dir.ReplaceAccess(new[]
        {
            new UserAccess("BOSCH\\QMMA", "QMM", 7, UserFunction.Member), new UserAccess("BOSCH\\QMMB", "QMM", 7, UserFunction.Member),
            new UserAccess("BOSCH\\OSDA", "OSD", 7, UserFunction.Member),
        });
        var process = new IbProcess { ValueStreamId = 7, CreatedBy = "BOSCH\\QMMA" };
        var pallet = process.NewPallet("BOSCH\\QMMA");
        const string category = nameof(PalletReturn.StatusCategoryCode);
        Assert.True(FieldPermissions.CanEdit(dir.ContextFor("BOSCH\\QMMA"), process, pallet, category));    // created it
        Assert.False(FieldPermissions.CanEdit(dir.ContextFor("BOSCH\\QMMB"), process, pallet, category));   // another QMM member
        Assert.True(FieldPermissions.CanComment(dir.ContextFor("BOSCH\\QMMB"), process));                  // but comments
        Assert.True(FieldPermissions.CanEdit(dir.ContextFor("BOSCH\\OSDA"), process, pallet, category));    // other areas: whole VS
        process.QmmResponsibleNtUser = "BOSCH\\QMMB";
        Assert.True(FieldPermissions.CanEdit(dir.ContextFor("BOSCH\\QMMB"), process, pallet, category));    // now QMM Responsible
    }

    [Fact]
    public void Owner_is_configured_by_windows_user_and_ignores_department_and_value_stream()
    {
        var dir = new UserDirectory((string?)null, new[] { "owner1" }, includeDemoUsers: false);
        Assert.True(dir.IsOwner("BOSCH\\OWNER1"));                 // configured without domain
        var owner = dir.ContextFor("BOSCH\\OWNER1");
        Assert.True(owner.IsOwner);
        Assert.True(FieldPermissions.CanEdit(owner, Imported(9), nameof(PalletReturn.HseScrapKg)));

        dir.ReplaceAccess(new[] { new UserAccess("BOSCH\\SECOND", "", 0, UserFunction.Owner) });
        Assert.True(dir.IsOwner("bosch\\second"));                 // Owner assigned in the access table
    }

    [Fact]
    public void Profile_changes_wait_for_the_owner_and_a_department_change_resets_functions()
    {
        var dir = new UserDirectory((string?)null, new[] { "BOSCH\\OWNER" }, includeDemoUsers: false);
        dir.Register("BOSCH\\U1", "User One", "QMM", new[] { 1 });
        dir.ReplaceAccess(new[] { new UserAccess("BOSCH\\U1", "QMM", 1, UserFunction.Champion) });

        // Same department, one more VS: request pending, access unchanged
        var req = dir.RequestChange("BOSCH\\U1", "QMM", new[] { 1, 5 });
        Assert.Equal(new[] { 1 }, dir.ContextFor("BOSCH\\U1").ValueStreams);
        Assert.NotNull(dir.PendingRequestFor("BOSCH\\U1"));

        dir.Decide(req.Id, approve: true, "BOSCH\\OWNER", null);
        var u = dir.ContextFor("BOSCH\\U1");
        Assert.Contains(u.Grants, g => g.ValueStreamId == 1 && g.Function == UserFunction.Champion);   // kept
        Assert.Contains(u.Grants, g => g.ValueStreamId == 5 && g.Function == UserFunction.Member);     // new VS: Member (the user already captures)

        // Department change: Champion/Proxy go back to Member
        var req2 = dir.RequestChange("BOSCH\\U1", "MFO", new[] { 1 });
        dir.Decide(req2.Id, approve: true, "BOSCH\\OWNER", null);
        u = dir.ContextFor("BOSCH\\U1");
        Assert.Equal("MFO", u.DepartmentCode);
        Assert.All(u.Grants, g => Assert.Equal(UserFunction.Member, g.Function));

        // Rejected request changes nothing
        var req3 = dir.RequestChange("BOSCH\\U1", "LOW", new[] { 9 });
        dir.Decide(req3.Id, approve: false, "BOSCH\\OWNER", "No");
        Assert.Equal("MFO", dir.ContextFor("BOSCH\\U1").DepartmentCode);
    }

    [Fact]
    public void Real_users_are_saved_to_the_file_and_demo_users_are_not()
    {
        var path = Path.Combine(Path.GetTempPath(), $"users_{Guid.NewGuid():N}.json");
        try
        {
            var dir = new UserDirectory(path, Array.Empty<string>(), includeDemoUsers: true);
            dir.Register("BOSCH\\REAL1", "Real User", "LOW", new[] { 9 });

            var reloaded = new UserDirectory(path, Array.Empty<string>(), includeDemoUsers: false);
            Assert.NotNull(reloaded.Find("BOSCH\\REAL1"));
            Assert.Single(reloaded.Users());                       // no DEMO\ users in the file
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Demo_users_have_a_dedicated_champion_per_area_and_value_stream()
    {
        var dir = new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true);
        var access = dir.Access();
        var areas = new[] { RoleCode.Qmm, RoleCode.Requestor, RoleCode.Gs, RoleCode.Lop, RoleCode.Mfo, RoleCode.Low, RoleCode.Hse };
        foreach (var area in areas)
            foreach (var vs in Catalogs.ValueStreams.Select(v => v.Id))
                Assert.Contains(access, a => a.ValueStreamId == vs && a.Function == UserFunction.Champion && RoleInfo.AreaOfDepartment(a.DepartmentCode) == area);
        Assert.Equal("Andrea Salinas", dir.QmmFor(1)[0]);                // Champion first, then Proxies
        Assert.Contains("Andrea Salinas", dir.QmmFor(7));          // Proxy in VS7 too
    }
}

public class PersonNameTests
{
    [Theory]
    [InlineData("EXTERNAL Reyna Morisco Luis David (PM, ME/QMM-L2-JuP1)", "Reyna Morisco Luis David")]
    [InlineData("Castillo Ivonne (ME/QMM1-JuP1)", "Castillo Ivonne")]
    [InlineData("  Lozano  Patricia  ", "Lozano Patricia")]
    [InlineData("EXTERNAL (PM)", null)]
    [InlineData("RLN8JU", null)]                         // only the account: never used as the name
    [InlineData(null, null)]
    public void The_windows_display_name_gives_the_person_name(string? display, string? expected) =>
        Assert.Equal(expected, PersonName.FromDisplayName(display, "US\\RLN8JU"));
}
