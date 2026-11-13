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
    [InlineData("MFQ4", RoleCode.Requestor)]          // Excel 2.0 list, active again
    [InlineData("LOW2", RoleCode.Low)]
    [InlineData("MFQ", RoleCode.Requestor)]           // old code: inactive, still valid for history
    public void Department_list_maps_to_the_permission_area(string code, RoleCode area) =>
        Assert.Equal(area, RoleInfo.AreaOfDepartment(code));

    [Fact]
    public void Only_the_new_department_list_is_offered()
    {
        var offered = Catalogs.Departments.Select(d => d.Code).ToList();
        Assert.Equal(new[] { "MFQ8", "MFO1", "MFO12", "MFO1-US", "MFE5", "QMM1", "QMM2", "LOP1", "LOP2", "LOW", "CTG3", "TEF6",
                             "GS/OSD43", "HSE", "MFQ1", "MFQ4", "MFQ5", "MFQ7", "MFQ9", "QMM-C1", "QMM-C2", "PUQ", "LOW2",
                             "QMM-L2", "DBE", "TEF1" }, offered);
        Assert.DoesNotContain("MFQ", offered);
        Assert.Contains(Catalogs.BuiltInDepartments, d => d.Code == "MFQ" && !d.IsActive);
        Assert.Equal(37, Catalogs.BuiltInDepartments.Count);
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
        if (SharedServersCredentials.IsEmbedded) return;     // embedded in this build: there is no file to miss
        var c = new SharedServersCredentials(new OracleSettings { SharedServersDll = Path.Combine(Path.GetTempPath(), "nope", "SharedServersAccess.dll") });
        var ex = Assert.Throws<CredentialsException>(() => c.Initialize("key"));
        Assert.Contains("not found", ex.Message);
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
        foreach (var d in Catalogs.BuiltInDepartments)
            Assert.Contains($"VALUES ('{d.Code}', '{d.Area}', '{(d.CanRequest ? "Y" : "N")}', '{d.Group}', '{(d.IsActive ? "Y" : "N")}');", seed);
        var areas = Regex.Match(Sql("01_create_tables.sql"), @"IB_DEPT_CK_AREA CHECK \(AREA IN \(([^)]*)\)").Groups[1].Value;
        foreach (var a in Catalogs.BuiltInDepartments.Select(d => d.Area).Distinct()) Assert.Contains($"'{a}'", areas);
    }

    [Theory]
    [InlineData("VS7!row 120", "VS7", 120)]
    [InlineData("VS1!fila 9", "VS1", 9)]
    public void Legacy_source_splits_into_sheet_and_row(string source, string sheet, int row) =>
        Assert.Equal((sheet, (int?)row), OraclePalletRepository.SplitLegacy(source));
}

/// <summary>
/// Static checks of the Oracle install script (no database needed): every name, reference and seed row is coherent
/// with itself and with the SQL the application sends.
/// </summary>
public class OracleScriptCoherenceTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpoReturn.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ExpoReturn.sln");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Ddl => Read("database", "oracle", "01_create_tables.sql");

    private static string NoComments(string sql) =>
        Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline), @"--[^\n]*", "");

    /// <summary>Table → (column → type) in creation order.</summary>
    private static List<(string Table, Dictionary<string, string> Columns, string Body)> Tables()
    {
        var list = new List<(string, Dictionary<string, string>, string)>();
        foreach (Match m in Regex.Matches(NoComments(Ddl), @"CREATE TABLE (IB_PROCESS_\w+) \((.*?)\n\);", RegexOptions.Singleline))
        {
            var cols = new Dictionary<string, string>();
            foreach (Match c in Regex.Matches(m.Groups[2].Value, @"^ {4}([A-Z][A-Z0-9_]*)\s+([A-Z0-9]+(?:\(\d+(?:,\d+| CHAR)?\))?)", RegexOptions.Multiline))
                if (c.Groups[1].Value != "CONSTRAINT") cols[c.Groups[1].Value] = c.Groups[2].Value;
            list.Add((m.Groups[1].Value, cols, m.Groups[2].Value));
        }
        return list;
    }

    private static Dictionary<string, string> ColumnsOf(string table) => Tables().Single(t => t.Table == table).Columns;

    [Fact]
    public void There_are_19_tables_and_every_name_fits_30_characters()
    {
        Assert.Equal(19, Tables().Count);
        var all = Read("database", "oracle", "IB_PROCESS_install.sql");
        var names = Regex.Matches(all, @"(?:TABLE|CONSTRAINT|INDEX|TRIGGER)\s+(IB_\w+)").Select(m => m.Groups[1].Value)
            .Concat(Tables().SelectMany(t => t.Columns.Keys));
        Assert.All(names, n => Assert.True(n.Length <= 30, $"{n} has {n.Length} characters"));
        var constraints = Regex.Matches(NoComments(Ddl), @"CONSTRAINT (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(constraints.Count, constraints.Distinct().Count());
    }

    [Fact]
    public void Text_columns_count_characters_not_bytes() =>
        Assert.DoesNotMatch(@"\bVARCHAR2\(\d+\)", NoComments(Ddl));

    /// <summary>
    /// Bind variables (":name") cannot be reserved words: Oracle answers ORA-01745 "invalid host/bind variable name".
    /// That is what refused the whole Excel import with ":row" (and ":by", ":comment" in other statements).
    /// </summary>
    [Fact]
    public void No_bind_variable_uses_an_oracle_reserved_word()
    {
        var dir = Path.Combine(Root(), "src", "ExpoReturn.Infrastructure.Oracle");
        var code = string.Join("\n", Directory.GetFiles(dir, "*.cs").Select(File.ReadAllText));
        var added = Regex.Matches(code, @"Parameters\.Add\(""([A-Za-z]\w*)""").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(code, @",\s*\(""([a-z]\w*)"",").Select(m => m.Groups[1].Value))   // Query(sql, map, ("name", value))
            .Distinct().ToList();
        var used = Regex.Matches(code, @"[\s(,=]:([A-Za-z]\w*)").Select(m => m.Groups[1].Value)
            .Where(b => b != "p").Distinct().ToList();                // :p0..:pN = the pallet columns, added in a loop
        Assert.True(added.Count > 40 && used.Count > 40);
        Assert.All(added.Concat(used), b => Assert.False(Reserved.Contains(b.ToUpperInvariant()), $":{b} is an Oracle reserved word (ORA-01745)"));
        Assert.All(added, b => Assert.Contains(b, used));            // every parameter given is used in a statement
        Assert.All(used, b => Assert.Contains(b, added));            // every :bind has its parameter
    }

    private static readonly HashSet<string> Reserved = new("ACCESS ADD ALL ALTER AND ANY AS ASC AUDIT BETWEEN BY CHAR CHECK CLUSTER COLUMN COMMENT COMPRESS CONNECT CREATE CURRENT DATE DECIMAL DEFAULT DELETE DESC DISTINCT DROP ELSE EXCLUSIVE EXISTS FILE FLOAT FOR FROM GRANT GROUP HAVING IDENTIFIED IMMEDIATE IN INCREMENT INDEX INITIAL INSERT INTEGER INTERSECT INTO IS LEVEL LIKE LOCK LONG MAXEXTENTS MINUS MLSLABEL MODE MODIFY NOAUDIT NOCOMPRESS NOT NOWAIT NULL NUMBER OF OFFLINE ON ONLINE OPTION OR ORDER PCTFREE PRIOR PRIVILEGES PUBLIC RAW RENAME RESOURCE REVOKE ROW ROWID ROWNUM ROWS SELECT SESSION SET SHARE SIZE SMALLINT START SUCCESSFUL SYNONYM SYSDATE TABLE THEN TO TRIGGER UID UNION UNIQUE UPDATE USER VALIDATE VALUES VARCHAR VARCHAR2 VIEW WHENEVER WHERE WITH".Split(' '));

    [Fact]
    public void No_column_uses_an_oracle_reserved_word()
    {
        var reserved = new HashSet<string>("ACCESS ADD ALL ALTER AND ANY AS ASC AUDIT BETWEEN BY CHAR CHECK CLUSTER COLUMN COMMENT COMPRESS CONNECT CREATE CURRENT DATE DECIMAL DEFAULT DELETE DESC DISTINCT DROP ELSE EXCLUSIVE EXISTS FILE FLOAT FOR FROM GRANT GROUP HAVING IDENTIFIED IMMEDIATE IN INCREMENT INDEX INITIAL INSERT INTEGER INTERSECT INTO IS LEVEL LIKE LOCK LONG MAXEXTENTS MINUS MLSLABEL MODE MODIFY NOAUDIT NOCOMPRESS NOT NOWAIT NULL NUMBER OF OFFLINE ON ONLINE OPTION OR ORDER PCTFREE PRIOR PRIVILEGES PUBLIC RAW RENAME RESOURCE REVOKE ROW ROWID ROWNUM ROWS SELECT SESSION SET SHARE SIZE SMALLINT START SUCCESSFUL SYNONYM SYSDATE TABLE THEN TO TRIGGER UID UNION UNIQUE UPDATE USER VALIDATE VALUES VARCHAR VARCHAR2 VIEW WHENEVER WHERE WITH".Split(' '));
        Assert.All(Tables().SelectMany(t => t.Columns.Keys.Select(c => $"{t.Table}.{c}")), c => Assert.DoesNotContain(c.Split('.')[1], reserved));
    }

    [Fact]
    public void Foreign_keys_point_to_tables_created_before_and_to_existing_columns()
    {
        var tables = Tables();
        for (var i = 0; i < tables.Count; i++)
            foreach (Match fk in Regex.Matches(tables[i].Body, @"FOREIGN KEY \((\w+)\) REFERENCES (\w+) \((\w+)\)"))
            {
                Assert.True(tables[i].Columns.ContainsKey(fk.Groups[1].Value), $"{tables[i].Table}.{fk.Groups[1].Value}");
                var target = tables.FindIndex(t => t.Table == fk.Groups[2].Value);
                Assert.True(target >= 0 && target < i, $"{tables[i].Table} references {fk.Groups[2].Value} before it exists");
                Assert.True(tables[target].Columns.ContainsKey(fk.Groups[3].Value));
                Assert.Equal(tables[target].Columns[fk.Groups[3].Value], tables[i].Columns[fk.Groups[1].Value]);   // same type
            }
    }

    [Fact]
    public void Seed_rows_use_existing_columns_and_respect_the_foreign_key_order()
    {
        var seed = Read("database", "oracle", "02_seed_data.sql");
        var seen = new List<string>();
        foreach (Match m in Regex.Matches(seed, @"INSERT INTO (\w+) \(([^)]*)\) VALUES \((.*)\);"))
        {
            var table = m.Groups[1].Value;
            var cols = m.Groups[2].Value.Split(',').Select(c => c.Trim()).ToList();
            Assert.All(cols, c => Assert.True(ColumnsOf(table).ContainsKey(c), $"{table}.{c}"));
            var body = Tables().Single(t => t.Table == table).Body;
            foreach (Match fk in Regex.Matches(body, @"FOREIGN KEY \((\w+)\) REFERENCES (\w+)"))
                if (cols.Contains(fk.Groups[1].Value) && fk.Groups[2].Value != table && !m.Groups[3].Value.Contains("NULL"))
                    Assert.True(seen.Contains(fk.Groups[2].Value), $"{table} is seeded before {fk.Groups[2].Value}");
            if (!seen.Contains(table)) seen.Add(table);
        }
        Assert.Contains("'US\\RLN8JU', NULL, NULL, 'OWNER'", seed);
        Assert.EndsWith("COMMIT;", seed.Trim());
    }

    [Fact]
    public void Field_permission_seed_is_the_same_matrix_the_application_uses()
    {
        var seed = Read("database", "oracle", "02_seed_data.sql");
        var rows = Regex.Matches(seed, @"INSERT INTO IB_PROCESS_FIELD_PERMISSIONS \(AREA_CODE, FIELD_NAME, STAGE_NO\) VALUES \('(\w+)', '(\w+)'")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToHashSet();
        var areas = new Dictionary<RoleCode, string>
        {
            [RoleCode.Qmm] = "QMM", [RoleCode.Requestor] = "REQUESTOR", [RoleCode.Gs] = "GS", [RoleCode.Lop] = "LOP",
            [RoleCode.Mfo] = "MFO", [RoleCode.Low] = "LOW", [RoleCode.Hse] = "HSE",
        };
        var app = areas.SelectMany(a => FieldPermissions.EditableFields(a.Key)
            .Select(f => (a.Value, OraclePalletRepository.ColumnOfProperty[f]))).ToHashSet();
        Assert.Equal(app.OrderBy(x => x).ToList(), rows.OrderBy(x => x).ToList());
        Assert.All(rows, r => Assert.True(ColumnsOf("IB_PROCESS_PALLET_RETURNS").ContainsKey(r.Item2)));
    }

    [Fact]
    public void Columns_written_by_the_application_exist_with_the_same_text_length()
    {
        var pallets = ColumnsOf("IB_PROCESS_PALLET_RETURNS");
        Assert.All(OraclePalletRepository.WrittenColumns, c => Assert.True(pallets.ContainsKey(c), c));
        foreach (var (col, max) in OraclePalletRepository.MaxLengths)
            Assert.Equal(max.ToString(), Regex.Match(pallets[col], @"^N?VARCHAR2\((\d+)(?: CHAR)?\)$").Groups[1].Value);
        // Virtual / identity columns are never written
        Assert.DoesNotContain("FOLIO_CODE", OraclePalletRepository.WrittenColumns);
        Assert.DoesNotContain("SAP_TOTAL_USD", OraclePalletRepository.WrittenColumns);
    }

    [Fact]
    public void Every_sql_statement_in_the_oracle_code_uses_existing_tables_and_columns()
    {
        var dir = Path.Combine(Root(), "src", "ExpoReturn.Infrastructure.Oracle");
        var code = string.Join("\n", Directory.GetFiles(dir, "*.cs").Select(File.ReadAllText));
        var tables = Tables().ToDictionary(t => t.Table, t => t.Columns);
        var checkedCount = 0;
        void Check(string table, IEnumerable<string> cols)
        {
            Assert.True(tables.ContainsKey(table), table);
            foreach (var c in cols.Select(c => c.Trim()).Where(c => c.Length > 0 && c != "*"))
            {
                Assert.True(tables[table].ContainsKey(c), $"{table}.{c}");
                checkedCount++;
            }
        }
        foreach (Match m in Regex.Matches(code, @"INSERT INTO \{_?db\.T\(""(\w+)""\)\}\s*\(([A-Z_,\s]+)\)"))
            Check(m.Groups[1].Value, m.Groups[2].Value.Split(','));
        foreach (Match m in Regex.Matches(code, @"SELECT ([A-Z_,\s]+?)\s+FROM \{_?db\.T\(""(\w+)""\)\}"))
            Check(m.Groups[2].Value, m.Groups[1].Value.Split(','));
        foreach (Match m in Regex.Matches(code, @"UPDATE \{_?db\.T\(""(\w+)""\)\}(.*?)(?:"", tx\)|"";)", RegexOptions.Singleline))
        {
            // sub-queries inside SET belong to their own table: left out here
            var set = Regex.Replace(m.Groups[2].Value, @"\(SELECT.*?\)\)", "", RegexOptions.Singleline);
            Check(m.Groups[1].Value, Regex.Matches(set, @"\b([A-Z][A-Z0-9_]+)\s*=").Select(x => x.Groups[1].Value)
                .Where(c => c is not "SYSDATE"));
        }
        foreach (Match m in Regex.Matches(code, @"WHERE ([^""]+?)(?:ORDER BY|RETURNING|"")"))
            foreach (Match c in Regex.Matches(m.Groups[1].Value, @"\b([A-Z][A-Z0-9_]{2,})\s*(?:=|IS |<=|>=)"))
                Assert.True(c.Groups[1].Value is "ROWNUM" or "SYSDATE" || tables.Values.Any(t => t.ContainsKey(c.Groups[1].Value)),
                    $"WHERE column {c.Groups[1].Value} is in no table");
        Assert.True(checkedCount > 60, $"only {checkedCount} columns checked");
    }

    [Fact]
    public void Department_change_of_2026_11_12_matches_the_catalog()
    {
        var change = Read("database", "oracle", "changes", "2026-11-12_departments_excel_list.sql");
        foreach (var code in new[] { "MFQ1", "MFQ4", "MFQ5", "MFQ7", "MFQ9", "QMM-C1", "QMM-C2", "PUQ" })
        {
            Assert.Contains($"'{code}'", change);
            Assert.True(Catalogs.FindDepartment(code) is { IsActive: true, CanRequest: true }, code);
        }
        var low2 = Catalogs.FindDepartment("LOW2")!;
        Assert.Contains($"SELECT 'LOW2', '{low2.Area}', 'Y', '{low2.Group}', 'Y' FROM DUAL", change);
        Assert.Equal(26, Catalogs.Departments.Count);
        Assert.Contains("ACTIVE_26", change);
        Assert.DoesNotContain(change, c => c > 127 || c == '&');
        Assert.DoesNotMatch(@"\bDELETE\b", NoComments(change));
    }

    [Fact]
    public void Install_file_is_the_four_scripts_in_order_and_plain_ascii()
    {
        var install = Read("database", "oracle", "IB_PROCESS_install.sql");
        var pos = -1;
        foreach (var part in new[] { "01_create_tables.sql", "02_seed_data.sql", "03_comments_and_triggers.sql", "04_integrity_rules.sql" })
        {
            var text = Read("database", "oracle", part).Trim();
            var at = install.IndexOf(text, StringComparison.Ordinal);
            Assert.True(at > pos, $"{part} missing or out of order: run python3 tools/database/build_all.py");
            pos = at;
            Assert.DoesNotContain(text, c => c > 127);
        }
        Assert.False(File.Exists(Path.Combine(Root(), "database", "oracle", "05_upgrade_from_v0_2.sql")));
    }

    [Fact]
    public void Verify_script_expects_every_table_column_and_trigger_and_only_reads()
    {
        var verify = Read("database", "oracle", "90_verify_install.sql");
        foreach (var (table, cols, _) in Tables())
            foreach (var c in cols.Keys) Assert.Contains($"'{table}', '{c}', ", verify);
        foreach (Match t in Regex.Matches(Read("database", "oracle", "IB_PROCESS_install.sql"), @"CREATE OR REPLACE TRIGGER (\w+)"))
            Assert.Contains($"SELECT '{t.Groups[1].Value}', ", verify);
        Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE)\b", NoComments(verify));
        Assert.DoesNotContain(verify, c => c > 127 || c == '&');
    }

    [Fact]
    public void Comments_and_integrity_triggers_name_existing_columns()
    {
        var comments = Read("database", "oracle", "03_comments_and_triggers.sql");
        foreach (Match m in Regex.Matches(comments, @"COMMENT ON COLUMN (\w+)\.(\w+)"))
            Assert.True(ColumnsOf(m.Groups[1].Value).ContainsKey(m.Groups[2].Value), m.Value);
        var rules = Read("database", "oracle", "04_integrity_rules.sql");
        foreach (Match t in Regex.Matches(rules, @"BEFORE UPDATE ON (\w+)\s+FOR EACH ROW\s+BEGIN(.*?)END;", RegexOptions.Singleline))
            foreach (Match c in Regex.Matches(t.Groups[2].Value, @":OLD\.(\w+)"))
            {
                Assert.True(ColumnsOf(t.Groups[1].Value).ContainsKey(c.Groups[1].Value), $"{t.Groups[1].Value}.{c.Groups[1].Value}");
                var body = Tables().Single(x => x.Table == t.Groups[1].Value).Body;
                Assert.DoesNotMatch(@"NOT NULL|GENERATED", Regex.Match(body, $@"\n\s+{c.Groups[1].Value}\s[^\n]*").Value);
            }
    }
}

/// <summary>Excel 2.0 rows are imported as working records that can be edited to homogenize the data.</summary>
public class ImportedRecordTests
{
    private static PalletReturn Imported(int folio, int row) => new()
    {
        ValueStreamId = 4, FolioNumber = folio, IsLegacy = true, LegacySource = $"VS4!row {row}", CreatedBy = "MIGRATION",
        ReturnedMaterial = "0203.306.644-250", QtyImported = 10, UnitCostSource = "PENDING",
        ImportDate = new DateTime(2026, 1, 10), ImportInvoice = "MPS26000001", ImportDateElpJup1 = new DateTime(2026, 1, 12),
        OldMaterialDeliveredQty = 8, ScrapQty = 5, ScrapDate = new DateTime(2026, 2, 1),   // 8 + 5 > 10: the Excel had it wrong
    };

    private static (InMemoryPalletRepository, InMemoryAuditTrail, PalletReturnService, PalletReturn) Setup()
    {
        var repo = new InMemoryPalletRepository();
        var audit = new InMemoryAuditTrail();
        var svc = new PalletReturnService(repo, audit, new SystemClock());
        var p = Imported(15, 20);
        PalletEvaluator.Apply(p, svc.Parameters, DateTime.Today);
        repo.AppendLegacy(new[] { p });
        return (repo, audit, svc, repo.GetAll().Single());
    }

    private static readonly UserContext Mfo4 = new("BOSCH\\MFO4", "MFO champion", "MFO1", false,
        new[] { new AccessGrant("MFO1", 4, UserFunction.Champion) });

    [Fact]
    public void Imported_records_are_editable_with_the_normal_permissions()
    {
        var (_, _, _, p) = Setup();
        Assert.False(p.Status.IsClosed());
        Assert.True(FieldPermissions.CanEdit(Mfo4, p, nameof(PalletReturn.ScrapQty)));
        Assert.False(FieldPermissions.CanEdit(Mfo4, p, nameof(PalletReturn.ImportInvoice)));     // still only its own area
    }

    [Fact]
    public void Errors_the_excel_already_had_do_not_block_but_new_errors_do()
    {
        var (repo, audit, svc, p) = Setup();
        var fix = p.Clone();
        fix.NewHU = "1234567890";                       // unrelated change, the balance error is still there
        var shown = svc.Evaluate(fix);
        Assert.Contains(shown.Issues, i => i.Code == "VAL-033" && i.Severity == Severity.Warning && i.FromExcel);
        var ok = svc.Save(fix, Mfo4);
        Assert.True(ok.Success, ok.Summary);
        Assert.Contains(audit.For(p.Id), a => a.Field == nameof(PalletReturn.NewHU));

        var worse = repo.Get(p.Id)!.Clone();
        worse.ScrapDate = new DateTime(2026, 1, 1);    // before the crossing: a NEW error
        var refused = svc.Save(worse, Mfo4);
        Assert.False(refused.Success);
        Assert.Contains(refused.Issues, i => i.Code == "VAL-035" && i.Severity == Severity.Error);

        var homogenized = repo.Get(p.Id)!.Clone();
        homogenized.ScrapQty = 2;                       // 8 + 2 = 10: the Excel error is corrected
        Assert.True(svc.Save(homogenized, Mfo4).Success);
        Assert.DoesNotContain(PalletValidator.Validate(repo.Get(p.Id)!, DateTime.Today), i => i.Code == "VAL-033");
    }

    [Fact]
    public void A_second_import_never_overwrites_edited_records()
    {
        var (repo, _, svc, p) = Setup();
        var e = p.Clone(); e.NewHU = "1234567890";
        Assert.True(svc.Save(e, Mfo4).Success);
        var again = repo.AppendLegacy(new[] { Imported(15, 20) });
        Assert.Equal(0, again.Added);
        Assert.Equal(1, again.AlreadyLoaded);
        Assert.Equal("1234567890", repo.Get(p.Id)!.NewHU);
    }

    [Fact]
    public void A_note_typed_in_a_value_cell_is_kept_in_the_findings_instead_of_refusing_the_row()
    {
        var p = Imported(45, 51);
        p.DeliveryNumber = "Scrap entregado para validaciones de MFE, CAMBIO DE REGIMEN CONFIRMADO POR RAMON";
        Assert.Equal(new[] { "DELIVERY_NUMBER" }, OraclePalletRepository.KeepTooLongInFindings(p));
        Assert.Null(p.DeliveryNumber);
        Assert.Contains("DQ-TOO-LONG:DELIVERY_NUMBER=Scrap entregado para validaciones de MFE, CAMBIO DE REGIMEN CONFIRMADO POR RAMON", p.DataQualityFlags);
        Assert.Empty(OraclePalletRepository.KeepTooLongInFindings(p));       // nothing left too long
    }

    /// <summary>Runs only when EXPORETURN_LEGACY_XLSX points to the real Excel (not versioned).</summary>
    [Fact]
    public void Every_row_of_the_real_excel_fits_the_oracle_columns()
    {
        var path = Environment.GetEnvironmentVariable("EXPORETURN_LEGACY_XLSX");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var rows = LegacyExcelImporter.Import(path, DateTime.Today).Pallets;
        foreach (var p in rows)
        {
            OraclePalletRepository.KeepTooLongInFindings(p);
            foreach (var (col, max) in OraclePalletRepository.MaxLengths)
            {
                var prop = OraclePalletRepository.ColumnOfProperty.FirstOrDefault(kv => kv.Value == col).Key;
                if (prop is not null && typeof(PalletReturn).GetProperty(prop)!.GetValue(p) is string v)
                    Assert.True(v.Trim().Length <= max, $"{p.LegacySource} {col}");
            }
            Assert.True(p.DataQualityText.Length <= 400, $"{p.LegacySource} findings {p.DataQualityText.Length}");
            Assert.True(p.LowExportNumber is null or (> 0 and <= 9999), $"{p.LegacySource} LOW export {p.LowExportNumber}");
        }
    }

    [Fact]
    public void Mm60_completes_the_cost_of_imported_records()
    {
        var (repo, audit, svc, p) = Setup();
        var costs = new InMemoryMaterialCostCatalog();
        costs.Replace(new[] { new MaterialCost("0203.306.644-250", "PN", 25, 10, "USD") }, "MM60");
        Assert.Equal(1, Mm60Import.CompletePendingCosts(costs, repo, audit, new SystemClock(), svc.Parameters, "test"));
        Assert.Equal(2.5m, repo.Get(p.Id)!.UnitCostUsd);
        Assert.Equal("MM60", repo.Get(p.Id)!.UnitCostSource);
    }

    [Fact]
    public void New_folios_continue_after_the_imported_ones_and_skip_the_reserved_range()
    {
        var (repo, _, _, _) = Setup();
        repo.AppendLegacy(new[] { Imported(900033, 33) });
        Assert.Equal(16, repo.NextFolio(4));            // after folio 15 of the Excel, not after 900033
    }
}

[CollectionDefinition("Global catalogs", DisableParallelization = true)]
public class GlobalCatalogsCollection { }

/// <summary>With the database, departments come from IB_PROCESS_DEPARTMENTS: no new executable to change them.</summary>
[Collection("Global catalogs")]
public class DepartmentsFromDatabaseTests
{
    [Fact]
    public void The_department_list_of_the_database_replaces_the_built_in_one()
    {
        try
        {
            Catalogs.UseDepartments(new[]
            {
                OracleCatalogs.ToDepartment("MFQ4", "MFQ", "Y", "PROCESS", "Y"),
                OracleCatalogs.ToDepartment("NEW1 ", "LOW", "Y", "PROCESS", "Y"),        // added in the database only
                OracleCatalogs.ToDepartment("QMM1", "QMM", "Y", "PROCESS", "N"),         // deactivated in the database
                OracleCatalogs.ToDepartment("DBE", "DBE", "N", "CROSS_FUNCTIONAL", "Y"),
            });
            Assert.Equal(new[] { "MFQ4", "NEW1", "DBE" }, Catalogs.Departments.Select(d => d.Code));
            Assert.Equal(new[] { "MFQ4", "NEW1" }, Catalogs.Departments.Where(d => d.CanRequest).Select(d => d.Code));
            Assert.Equal(RoleCode.Low, RoleInfo.AreaOfDepartment("NEW1"));
            Assert.Equal(RoleCode.Qmm, RoleInfo.AreaOfDepartment("QMM1"));       // inactive: still valid for its records
            Assert.Equal(Department.CrossFunctional, Catalogs.FindDepartment("DBE")!.Group);

            Catalogs.UseDepartments(Array.Empty<Department>());                   // an empty answer never empties the list
            Assert.Equal(3, Catalogs.Departments.Count);
        }
        finally { Catalogs.UseBuiltIn(); }
        Assert.Equal(26, Catalogs.Departments.Count);
    }
}

/// <summary>v0.4.0: the database connection is mandatory and automatic.</summary>
public class MandatoryConnectionTests
{
    [Fact]
    public async Task After_a_failed_start_it_retries_now_then_after_5_and_10_seconds()
    {
        var waits = new List<TimeSpan>();
        var announced = new List<ConnectionRetry.RetryInfo>();
        var calls = 0;
        var outcome = await ConnectionRetry.RetryAsync<string>(() => ++calls < 3 ? throw new InvalidOperationException($"down {calls}") : "connected",
            "first failure", announced.Add, (d, _) => { waits.Add(d); return Task.CompletedTask; }, CancellationToken.None);
        Assert.True(outcome.Connected);
        Assert.Equal(4, outcome.Attempts);                          // first attempt + 3 retries
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }, waits);   // retry 1 does not wait
        Assert.Equal(new[] { "first failure", "down 1", "down 2" }, announced.Select(a => a.LastError));
        Assert.Equal(new[] { 0, 5, 10 }, ConnectionRetry.RetryDelays.Select(d => (int)d.TotalSeconds));
    }

    [Fact]
    public async Task When_every_retry_fails_there_is_no_connection_and_the_last_error_is_shown()
    {
        var outcome = await ConnectionRetry.RetryAsync<string>(() => throw new System.Reflection.TargetInvocationException(new Exception("ORA-12154: no alias")),
            "first", null, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.False(outcome.Connected);
        Assert.Equal("ORA-12154: no alias", outcome.LastError);
        Assert.Equal(4, outcome.Attempts);
    }

    [Fact]
    public async Task Closing_the_window_stops_the_retries()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectionRetry.RetryAsync<string>(
            () => { calls++; throw new Exception("down"); }, "first", _ => { }, (_, _) => { cts.Cancel(); return Task.CompletedTask; }, cts.Token));
        Assert.Equal(1, calls);                                     // retry 1 ran, the wait before retry 2 was cancelled
    }

    /// <summary>
    /// The user only needs the executable: SharedServersAccess.dll is embedded and loaded from memory. Only metadata is
    /// read here (no method of the DLL runs). Skipped when lib/SharedServersAccess.dll was not there at build time.
    /// </summary>
    [Fact]
    public void SharedServersAccess_travels_inside_the_application()
    {
        Assert.Equal("1234", OracleSettings.MasterKey);
        var lib = Path.Combine(OracleScriptRoot(), "lib", "SharedServersAccess.dll");
        if (!File.Exists(lib)) return;
        Assert.True(SharedServersCredentials.IsEmbedded);
        var asm = SharedServersCredentials.LoadAssembly(Path.Combine(Path.GetTempPath(), "does-not-exist", "SharedServersAccess.dll"));
        Assert.Equal("SharedServersAccess", asm.GetName().Name);
        Assert.NotNull(asm.GetType("SharedServersAccess.AccessControl")!.GetMethod("InicializarClaveMaestra", new[] { typeof(string) }));
        Assert.NotNull(asm.GetType("SharedServersAccess.DiccionarioService")!.GetMethod("ObtenerCredencialesDeServidor", new[] { typeof(string), typeof(bool) }));
        Assert.Same(asm, SharedServersCredentials.LoadAssembly("x"));          // loaded once per process
    }

    private static string OracleScriptRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpoReturn.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}

/// <summary>All the catalogs come from the database (managed there, no new executable).</summary>
[Collection("Global catalogs")]
public class CatalogsFromDatabaseTests
{
    [Fact]
    public void Value_streams_reasons_categories_status_names_and_permissions_come_from_the_database()
    {
        try
        {
            Catalogs.Use(new CatalogData(
                new[] { new ValueStream(1, "VS1", "Value Stream 1"), new ValueStream(4, "VS4", "Value Stream 4", IsActive: false), new ValueStream(12, "VS12", "Value Stream 12") },
                new[] { new Reason(1, "R01", "Packaging change due to customer request", "PACKAGING", false),
                        new Reason(3, "R03", "To change FG P/N with reflash", "REFLASH", true, IsActive: false) },
                new[] { new StatusCategory("EXPORT", "EXPORT", "OUTCOME", null, false, true),
                        new StatusCategory("SCRAP", "SCRAP", "OUTCOME", null, false, true),
                        new StatusCategory("ENVIO NACIONAL", "ENVIO NACIONAL", "OUTCOME", "GS", false, false, IsActive: false) },
                new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "SCRAP", "EXPORT", "ENVIO NACIONAL" } },
                Array.Empty<Department>(),
                new Dictionary<WorkflowStatus, string> { [WorkflowStatus.Exported] = "Exported (closed)" }));
            FieldPermissions.UseMatrix(new Dictionary<RoleCode, IReadOnlyCollection<string>>
            {
                [RoleCode.Lop] = new[] { nameof(PalletReturn.DeliveryNumber), nameof(PalletReturn.ImportInvoice) },   // changed in the database
            });

            Assert.Equal(new[] { "VS1", "VS12" }, Catalogs.ValueStreams.Select(v => v.Code));          // VS4 deactivated, VS12 added
            Assert.Equal(new[] { "R01" }, Catalogs.Reasons.Select(r => r.Code));
            Assert.True(Catalogs.FindReason(3)!.RequiresNewMaterial);                                 // inactive, still valid for its records
            Assert.Equal(new[] { "SCRAP", "EXPORT" }, Catalogs.CommentsOptions(1));                   // VS order, inactive left out
            Assert.Contains("ENVIO NACIONAL", Catalogs.CommentsOptions(1, "ENVIO NACIONAL"));        // a record keeps its value
            Assert.Equal("Exported (closed)", WorkflowStatus.Exported.DisplayName());
            Assert.Equal("Draft", WorkflowStatus.Draft.DisplayName());                                // not in the database: built-in name
            var lop = new UserContext("T\\LOP", "LOP", "LOP1", false, new[] { new AccessGrant("LOP1", 1, UserFunction.Champion) });
            var p = new PalletReturn { ValueStreamId = 1, Status = WorkflowStatus.Imported };
            Assert.True(FieldPermissions.CanEdit(lop, p, nameof(PalletReturn.ImportInvoice)));
        }
        finally
        {
            Catalogs.UseBuiltIn();
            FieldPermissions.UseBuiltInMatrix();
        }
        Assert.Equal(5, Catalogs.ValueStreams.Count);
        Assert.Equal("Exported", WorkflowStatus.Exported.DisplayName());
    }
}

public class DepartmentAdminTests
{
    [Theory]
    [InlineData("MFQ3", "MFQ", "PROCESS", true)]
    [InlineData("gs/osd44", "GS", "PROCESS", true)]
    [InlineData("", "MFQ", "PROCESS", false)]
    [InlineData("MFQ 3", "MFQ", "PROCESS", false)]
    [InlineData("MFQ3", "XYZ", "PROCESS", false)]          // area the database would refuse
    [InlineData("MFQ3", "MFQ", "OTHER", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", "MFQ", "PROCESS", false)]
    public void New_departments_are_checked_before_reaching_the_database(string code, string area, string group, bool ok) =>
        Assert.Equal(ok, Department.Validate(code, area, group) is null);

    [Fact]
    public void Areas_offered_are_the_ones_the_database_accepts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "ExpoReturn.sln"))) dir = dir.Parent;
        var ddl = File.ReadAllText(Path.Combine(dir.FullName, "database", "oracle", "01_create_tables.sql"));
        var check = Regex.Match(ddl, @"IB_DEPT_CK_AREA CHECK \(AREA IN \(([^)]*)\)").Groups[1].Value
            .Split(',').Select(a => a.Trim().Trim('\'')).OrderBy(a => a);
        Assert.Equal(check, Department.Areas.Select(a => a.Area).OrderBy(a => a));
    }
}
