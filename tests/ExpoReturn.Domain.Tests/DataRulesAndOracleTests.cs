using System.Text.RegularExpressions;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using ExpoReturn.Infrastructure.Oracle;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>v0.3.0: nothing is deleted, filled fields are never emptied, new department list, Oracle mapping.</summary>
public class DataRulesAndOracleTests
{
    private static (InMemoryPalletRepository Repo, InMemoryAuditTrail Audit, PalletReturnService Svc) Seeded()
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var clock = new SystemClock();
        SampleData.Seed(repo, audit, new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true), clock);
        return (repo, audit, new PalletReturnService(repo, audit, clock));
    }

    private static readonly UserContext Owner = new("BOSCH\\OWNER", "Owner", null, true, Array.Empty<AccessGrant>());

    [Fact]
    public void Not_even_the_owner_can_empty_a_field_that_has_a_value()
    {
        var (repo, _, svc) = Seeded();
        var p = repo.GetAll().First(x => !x.IsLegacy && x.ImportInvoice is not null && !x.Status.IsClosed());
        var edit = p.Clone();
        edit.ImportInvoice = "  ";
        var r = svc.Save(edit, Owner, "test");
        Assert.False(r.Success);
        Assert.Contains(nameof(PalletReturn.ImportInvoice), r.Cleared);
        Assert.Equal(p.ImportInvoice, repo.Get(p.Id)!.ImportInvoice);        // unchanged

        var change = p.Clone();
        change.ImportInvoice = $"MPS{p.ImportDate:yy}123456";             // changing it is allowed
        Assert.True(svc.Save(change, Owner, "test").Success);
    }

    [Fact]
    public void A_stale_copy_is_refused_instead_of_overwriting_someone_elses_save()
    {
        var (repo, _, svc) = Seeded();
        var p = repo.GetAll().First(x => !x.IsLegacy && x.ImportDate is not null && !x.Status.IsClosed());
        var first = p.Clone();
        var second = p.Clone();
        first.ImportInvoice = $"MPS{p.ImportDate:yy}111111";
        second.ImportInvoice = $"MPS{p.ImportDate:yy}222222";
        Assert.True(svc.Save(first, Owner, "a").Success);
        var r = svc.Save(second, Owner, "b");
        Assert.False(r.Success);
        Assert.Contains("someone else", r.Summary);
        Assert.Equal(first.ImportInvoice, repo.Get(p.Id)!.ImportInvoice);
    }

    [Fact]
    public void Ended_access_and_replaced_requests_are_kept_as_history()
    {
        var path = Path.Combine(Path.GetTempPath(), $"users_{Guid.NewGuid():N}.json");
        try
        {
            var dir = new UserDirectory(path, new[] { "BOSCH\\OWNER" }, includeDemoUsers: false);
            dir.Register("BOSCH\\U2", "User Two", "MFO1", new[] { 1, 4 });
            dir.ReplaceAccess(new[] { new UserAccess("BOSCH\\U2", "MFO1", 1, UserFunction.Champion) }, "BOSCH\\OWNER");
            var first = dir.RequestChange("BOSCH\\U2", "MFO1", new[] { 1, 5 });
            dir.RequestChange("BOSCH\\U2", "MFO12", new[] { 1 });

            var json = File.ReadAllText(path);
            Assert.Contains("\"AccessHistory\"", json);
            Assert.Contains("BOSCH\\\\OWNER", json);                                 // who ended the VS4 viewer row and the VS1 viewer row
            Assert.Equal(2, dir.Requests(pendingOnly: false).Count);                 // the first request is still there…
            Assert.Equal(RequestStatus.Rejected, dir.Requests(false).Single(r => r.Id == first.Id).Status);   // …marked as replaced
            Assert.Throws<DataRuleException>(() => dir.UpdateDisplayName("BOSCH\\U2", " "));
            Assert.Throws<DataRuleException>(() => dir.RequestChange("BOSCH\\U2", "", new[] { 1 }));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_new_mm60_file_keeps_the_materials_it_does_not_contain()
    {
        var cat = new InMemoryMaterialCostCatalog();
        cat.Replace(new[] { new MaterialCost("0203.306.644-250", "A", 10, 1, "USD"), new MaterialCost("0203.306.645-250", "B", 20, 1, "USD") }, "first");
        cat.Replace(new[] { new MaterialCost("0203.306.644-250", "A", 12, 1, "USD") }, "second");
        Assert.Equal(12, cat.Find("0203.306.644-250")!.UnitCostUsd);
        Assert.Equal(20, cat.Find("0203.306.645-250")!.UnitCostUsd);
    }

    [Theory]
    [InlineData("MFQ8", RoleCode.Requestor)]
    [InlineData("MFE5", RoleCode.Requestor)]
    [InlineData("TEF6", RoleCode.Requestor)]
    [InlineData("QMM1", RoleCode.Qmm)]
    [InlineData("MFO1-US", RoleCode.Mfo)]
    [InlineData("LOP2", RoleCode.Lop)]
    [InlineData("LOW", RoleCode.Low)]
    [InlineData("GS/OSD43", RoleCode.Gs)]
    [InlineData("HSE", RoleCode.Hse)]
    [InlineData("CTG3", RoleCode.Viewer)]
    [InlineData("QMM-L2", RoleCode.Viewer)]
    [InlineData("DBE", RoleCode.Viewer)]
    [InlineData("MFQ4", RoleCode.Requestor)]          // old code: inactive, still valid for history
    public void Department_list_maps_to_the_permission_area(string code, RoleCode area) =>
        Assert.Equal(area, RoleInfo.AreaOfDepartment(code));

    [Fact]
    public void Only_the_new_department_list_is_offered()
    {
        var offered = Catalogs.Departments.Select(d => d.Code).ToList();
        Assert.Equal(new[] { "MFQ8", "MFO1", "MFO12", "MFO1-US", "MFE5", "QMM1", "QMM2", "LOP1", "LOP2", "LOW", "CTG3", "TEF6",
                             "GS/OSD43", "HSE", "QMM-L2", "DBE", "TEF1" }, offered);
        Assert.DoesNotContain("MFQ4", offered);
        Assert.Contains(Catalogs.AllDepartments, d => d.Code == "MFQ4" && !d.IsActive);
    }

    // ------------------------------------------------------------------ SharedServersAccess

    [Fact]
    public void Credentials_use_the_dll_properties_when_the_string_has_placeholders()
    {
        var login = SharedServersCredentials.Parse("IFACTJUP", "Data Source=IFACTJUP;User ID={userX};Password={passwordX}", null, "APPUSER", "secret");
        Assert.Equal(new OracleLogin("APPUSER", "secret", "IFACTJUP"), login);

        var inline = SharedServersCredentials.Parse("IFACTJUP", "", "data source = ifactjup ; user id=U1; password='p;w'", null, null);
        Assert.Equal(new OracleLogin("U1", "p;w", "ifactjup"), inline);
    }

    [Fact]
    public void A_wrong_master_key_shows_up_as_a_data_source_that_is_not_the_server()
    {
        var ex = Assert.Throws<CredentialsException>(() =>
            SharedServersCredentials.Parse("IFACTJUP", "Data Source=ÿ#garbage;User ID=x;Password=y", null, null, null));
        Assert.Contains("master key", ex.Message);
        Assert.Throws<CredentialsException>(() => SharedServersCredentials.Parse("IFACTJUP", "Data Source=IFACTJUP;User ID={userX}", null, "", ""));
    }

    [Fact]
    public void Missing_dll_gives_a_clear_message()
    {
        var c = new SharedServersCredentials(new OracleSettings { SharedServersDll = Path.Combine(Path.GetTempPath(), "nope", "SharedServersAccess.dll") });
        var ex = Assert.Throws<CredentialsException>(() => c.Initialize("key"));
        Assert.Contains("was not found", ex.Message);
    }

    // ------------------------------------------------------------------ code ↔ Oracle scripts

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpoReturn.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ExpoReturn.sln");
    }

    private static string Sql(string file) => File.ReadAllText(Path.Combine(Root(), "database", "oracle", file));

    [Fact]
    public void Every_mapped_column_exists_and_every_editable_field_is_mapped()
    {
        var ddl = Sql("01_create_tables.sql");
        var table = Regex.Match(ddl, @"CREATE TABLE IB_PROCESS_PALLET_RETURNS \((.*?)\n\);", RegexOptions.Singleline).Groups[1].Value;
        foreach (var col in OraclePalletRepository.ColumnOfProperty.Values)
            Assert.Matches(new Regex($@"^\s+{col}\s", RegexOptions.Multiline), table);
        foreach (var f in Fields.All) Assert.True(OraclePalletRepository.ColumnOfProperty.ContainsKey(f), f);
    }

    [Fact]
    public void Every_table_refuses_delete_and_alert_codes_match_the_check()
    {
        var ddl = Sql("01_create_tables.sql");
        var rules = Sql("04_integrity_rules.sql") + Sql("03_comments_and_triggers.sql");
        foreach (Match m in Regex.Matches(ddl, @"CREATE TABLE (IB_PROCESS_\w+)"))
            Assert.Matches(new Regex($@"BEFORE (UPDATE OR )?DELETE ON {m.Groups[1].Value}\b"), rules);
        Assert.Contains("IB_PR_NO_EMPTY", rules);
        var check = Regex.Match(ddl, @"IB_ALERT_CK_TYPE CHECK \(ALERT_TYPE IN \(([^)]*)\)").Groups[1].Value;
        foreach (var t in Enum.GetValues<AlertType>()) Assert.Contains($"'{OracleAlertStore.Code(t)}'", check);
    }

    [Fact]
    public void Department_seed_matches_the_catalog()
    {
        var seed = Sql("02_seed_data.sql");
        foreach (var d in Catalogs.AllDepartments)
            Assert.Contains($"VALUES ('{d.Code}', '{d.Area}', '{(d.CanRequest ? "Y" : "N")}', '{d.Group}', '{(d.IsActive ? "Y" : "N")}');", seed);
        var areas = Regex.Match(Sql("01_create_tables.sql"), @"IB_DEPT_CK_AREA CHECK \(AREA IN \(([^)]*)\)").Groups[1].Value;
        foreach (var a in Catalogs.AllDepartments.Select(d => d.Area).Distinct()) Assert.Contains($"'{a}'", areas);
    }

    [Theory]
    [InlineData("VS7!row 120", "VS7", 120)]
    [InlineData("VS1!fila 9", "VS1", 9)]
    public void Legacy_source_splits_into_sheet_and_row(string source, string sheet, int row) =>
        Assert.Equal((sheet, (int?)row), OraclePalletRepository.SplitLegacy(source));
}
