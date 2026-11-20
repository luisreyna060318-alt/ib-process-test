using System.Text.RegularExpressions;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>v0.5.7: the Windows user is kept without domain ("US\RLN8JU" → "RLN8JU"); old values still count as the same user.</summary>
public class UserWithoutDomainTests
{
    [Theory]
    [InlineData("US\\RLN8JU", "RLN8JU")]
    [InlineData("  us\\rln8ju ", "RLN8JU")]
    [InlineData("RLN8JU", "RLN8JU")]
    [InlineData("MX\\SUB\\ABC1JU", "ABC1JU")]
    [InlineData(null, "")]
    public void The_domain_is_removed_and_the_account_kept_in_capitals(string? nt, string expected) =>
        Assert.Equal(expected, NtUsers.Normalize(nt));

    [Fact]
    public void A_user_with_or_without_domain_is_the_same_user()
    {
        Assert.True(NtUsers.Same("US\\RLN8JU", "rln8ju"));
        Assert.False(NtUsers.Same("US\\RLN8JU", "RLN8JUX"));
        Assert.False(NtUsers.Same(null, ""));
    }

    [Fact]
    public void Directory_keeps_users_without_domain_and_finds_them_with_or_without_it()
    {
        var dir = new UserDirectory((string?)null, new[] { "US\\RLN8JU" }, includeDemoUsers: false);
        Assert.True(dir.IsOwner("RLN8JU"));
        Assert.True(dir.IsOwner("US\\RLN8JU"));
        dir.Register("US\\BAJ1JU", "Barrera Barrera Jose Ramon", "MFO", new[] { 1 });
        Assert.Equal("BAJ1JU", Assert.Single(dir.Users()).NtUser);
        Assert.NotNull(dir.Find("BAJ1JU"));
        Assert.NotNull(dir.Find("us\\baj1ju"));
        Assert.All(dir.Access(), a => Assert.DoesNotContain('\\', a.NtUser));
    }

    [Fact]
    public void A_process_saved_with_the_old_user_is_still_mine()
    {
        var qmmMember = new UserContext("WGE1JU", "Wong", "QMM", false, new[] { new AccessGrant("QMM", 1, UserFunction.Member) });
        var p = new IbProcess { ValueStreamId = 1, CreatedBy = "US\\WGE1JU" };
        p.AddPallet(new PalletReturn { ReturnedMaterial = "0203.306.644-250", QtyImported = 1, OriginalHU = "2104891840", ReturnPONumber = "49398082" });
        Assert.True(FieldPermissions.CanEdit(qmmMember, p, null, nameof(IbProcess.ReasonId)));
        Assert.True(ProcessFilter.IsMine(p, "WGE1JU"));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "ExpoReturn.sln"))) dir = dir.Parent;
        return dir.FullName;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact]
    public void Change_of_2026_11_20_moves_every_user_column_and_registers_the_champions_and_proxies()
    {
        var change = Read("database", "oracle", "changes", "2026-11-20_users_without_domain_and_area_leads.sql");
        var ddl = Read("database", "oracle", "01_create_tables.sql");
        Assert.DoesNotContain(change, c => c > 127 || c == '&');
        // Every column that keeps a user (VARCHAR2(64 CHAR)) is moved, except the audit log and the comments (never edited)
        var userColumns = new List<(string, string)>();
        string? table = null;
        foreach (var line in ddl.Split('\n'))
        {
            if (Regex.Match(line, @"^CREATE TABLE (\w+)") is { Success: true } t) table = t.Groups[1].Value;
            if (Regex.Match(line, @"^\s+([A-Z_]+)\s+VARCHAR2\(64 CHAR\)") is { Success: true } c && table is not null) userColumns.Add((table, c.Groups[1].Value));
        }
        foreach (var (t, c) in userColumns.Where(x => x.Item1 is not "IB_PROCESS_AUDIT_LOG" and not "IB_PROCESS_COMMENTS"))
            Assert.True(change.Contains($"UPDATE {t} SET {c} = ") || change.Contains($"UPDATE {t} SET {c} = r.SHORT_USER")
                        || (t == "IB_PROCESS_USERS" && c == "NT_USER"), $"{t}.{c}");
        Assert.DoesNotMatch(@"UPDATE IB_PROCESS_(AUDIT_LOG|COMMENTS)\b", change);
        // Same rule as a new install
        Assert.Contains("CONSTRAINT IB_USR_CK_NO_DOMAIN CHECK (INSTR(NT_USER, '\\') = 0)", ddl);
        Assert.Contains("CHECK (INSTR(NT_USER, ''\\'') = 0)", change);
        // The champions and proxies: 15 rows of one VS + 5 users in the five VS = 40 access rows, 20 users
        var leads = Regex.Matches(change, @"put_lead\('(\w+)', N'([^']+)', '(\w+)', '(\w+)', (v_all_vs\(i\)|\d)\);").ToList();
        Assert.Equal(20, leads.Count);
        Assert.Equal(20, leads.Select(m => m.Groups[1].Value).Distinct().Count());
        Assert.Equal(15 + 5 * 5, leads.Sum(m => m.Groups[5].Value == "v_all_vs(i)" ? 5 : 1));
        Assert.Contains("t_numbers(1, 4, 5, 7, 9)", change);
        Assert.All(leads, m => Assert.Contains(m.Groups[3].Value, new[] { "MFO", "QMM", "LOW", "OSD" }));
        Assert.All(leads, m => Assert.Contains(m.Groups[4].Value, new[] { "CHAMPION", "PROXY" }));
        Assert.Equal(2, leads.Count(m => m.Groups[3].Value == "LOW" && m.Groups[4].Value == "PROXY"));   // two proxies are allowed
        // Who made the change: the user without domain
        var code = Regex.Replace(change, @"/\*.*?\*/|--[^\n]*", "", RegexOptions.Singleline);
        Assert.DoesNotContain("'US\\RLN8JU'", code);
        Assert.Contains("'RLN8JU', '0.5.7'", change);
        Assert.DoesNotContain("OS_USER", change);
        Assert.DoesNotMatch(@"\b(DROP|TRUNCATE)\b", Regex.Replace(change, @"/\*.*?\*/|--[^\n]*", "", RegexOptions.Singleline));
    }
}
