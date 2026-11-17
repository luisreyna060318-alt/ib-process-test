using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>
/// The inbox reads one page at a time from the store (Oracle: views of 05_views.sql). These tests fix the rules the SQL
/// follows: stable pages, order with empty values last, filters, totals of the whole filter, status calculated today.
/// </summary>
public class InboxQueryTests
{
    private sealed class Clock(DateTime today) : IClock
    {
        public DateTime Today { get; set; } = today;
        public DateTime UtcNow => Today.ToUniversalTime();
    }

    private static (InMemoryProcessRepository Repo, InMemoryProcessQueries Queries, Clock Clock) Seeded()
    {
        var repo = new InMemoryProcessRepository();
        var clock = new Clock(DateTime.Today);
        SampleData.Seed(repo, new InMemoryAuditTrail(), new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true), clock);
        return (repo, new InMemoryProcessQueries(repo, clock), clock);
    }

    private static List<ProcessSummary> AllPages(IProcessQueries q, ProcessFilter f, ProcessSort s, int size)
    {
        var all = new List<ProcessSummary>();
        for (var skip = 0; ; skip += size)
        {
            var page = q.Page(f, s, skip, size);
            all.AddRange(page);
            if (page.Count < size) return all;
        }
    }

    [Fact]
    public void Pages_put_together_are_the_whole_filter_once_in_every_order()
    {
        var (repo, q, _) = Seeded();
        var count = q.Totals(ProcessFilter.All).Count;
        Assert.Equal(repo.GetAll().Count, count);
        foreach (var member in ProcessSort.Members.Cast<string?>().Append(null))
            foreach (var desc in new[] { false, true })
            {
                var sort = new ProcessSort(member, desc);
                var pages = AllPages(q, ProcessFilter.All, sort, 7);
                Assert.Equal(count, pages.Count);
                Assert.Equal(count, pages.Select(p => p.Id).Distinct().Count());              // no row twice, none lost
                Assert.Equal(pages.Select(p => p.Id), q.Page(ProcessFilter.All, sort, 0, 10_000).Select(p => p.Id));
            }
    }

    [Fact]
    public void Empty_values_go_last_in_both_directions_and_the_default_order_puts_the_nearest_due_date_first()
    {
        var (_, q, _) = Seeded();
        foreach (var desc in new[] { false, true })
        {
            var rows = q.Page(ProcessFilter.All, new ProcessSort("DueDate", desc), 0, 10_000).ToList();
            var firstEmpty = rows.FindIndex(r => r.NearestDueDate is null);
            Assert.True(firstEmpty > 0);
            Assert.All(rows.Skip(firstEmpty), r => Assert.Null(r.NearestDueDate));
            var dates = rows.Take(firstEmpty).Select(r => r.NearestDueDate!.Value).ToList();
            Assert.Equal(desc ? dates.OrderByDescending(d => d) : dates.OrderBy(d => d), dates);
        }
        var def = q.Page(ProcessFilter.All, ProcessSort.Default, 0, 10_000).ToList();
        var lastOpen = def.FindLastIndex(r => !r.IsClosed);
        Assert.All(def.Take(lastOpen + 1), r => Assert.False(r.IsClosed));                   // closed processes at the end
        var due = def.Where(r => r.DaysToDue is not null).Select(r => r.DaysToDue!.Value).ToList();
        Assert.Equal(due.OrderBy(d => d), due);
    }

    [Fact]
    public void Totals_count_the_whole_filter_not_the_page()
    {
        var (repo, q, clock) = Seeded();
        var f = new ProcessFilter(ValueStreamId: 7, OnlyOpen: true);
        var expected = repo.GetAll().Where(p => p.ValueStreamId == 7 && !p.Status.IsClosed()).ToList();
        var t = q.Totals(f);
        Assert.Equal(expected.Count, t.Count);
        Assert.Equal(expected.Count, t.Open);
        Assert.Equal(expected.Count(p => p.IsDutyRisk), t.DutyRisk);
        Assert.Equal(expected.Sum(p => p.BalanceGeneralUsd), t.Balance);
        Assert.True(q.Page(f, ProcessSort.Default, 0, 2).Count <= 2);
    }

    [Fact]
    public void Search_finds_a_process_by_the_po_hu_or_pallet_folio_of_any_of_its_pallets()
    {
        var (repo, q, _) = Seeded();
        var p = repo.GetAll().First(x => x.Pallets.Count > 1);
        var second = p.Pallets[1];
        foreach (var text in new[] { second.FolioCode, second.OriginalHU ?? second.OriginalBatch!, p.FolioCode.ToLowerInvariant() })
            Assert.Contains(q.Page(new ProcessFilter(Search: text), ProcessSort.Default, 0, 50), r => r.Id == p.Id);
        Assert.Empty(q.Page(new ProcessFilter(Search: "no-such-text-xyz"), ProcessSort.Default, 0, 50));
    }

    [Fact]
    public void Status_and_duty_risk_are_calculated_with_the_date_of_the_query_not_the_date_of_the_save()
    {
        var repo = new InMemoryProcessRepository();
        var clock = new Clock(new DateTime(2026, 1, 1));
        var svc = new ProcessService(repo, new InMemoryAuditTrail(), clock);
        var q = new InMemoryProcessQueries(repo, clock);
        var p = new IbProcess
        {
            ValueStreamId = 4, CreatedBy = "B\\X", RequestingDepartment = "MFQ", RequestorName = "X", QmmResponsible = "Q", ReasonId = 6,
            ImportDate = new DateTime(2025, 12, 1), ImportInvoice = "MPS25000001", ImportDateElpJup1 = new DateTime(2025, 12, 1),
        };
        p.AddPallet(new PalletReturn { ReturnedMaterial = "0203.306.644-250", QtyImported = 10, UnitCostUsd = 5, OriginalHU = "2104891840", ReturnPONumber = "49398082" });
        var owner = new UserContext("B\\OWNER", "Owner", null, true, Array.Empty<AccessGrant>());
        Assert.True(svc.Save(p, owner).Success);
        Assert.Equal(WorkflowStatus.Imported, q.Page(ProcessFilter.All, ProcessSort.Default, 0, 1).Single().Status);

        clock.Today = new DateTime(2025, 12, 1).AddDays(540 - 20);                           // 20 days left: duty risk
        var row = q.Page(ProcessFilter.All, ProcessSort.Default, 0, 1).Single();
        Assert.Equal(WorkflowStatus.DutyRisk, row.Status);
        Assert.True(row.IsDutyRisk);
        Assert.Equal(20, row.DaysToDue);
        Assert.Equal(1, q.Totals(new ProcessFilter(OnlyDutyRisk: true)).Count);
    }

    [Fact]
    public void To_homogenize_lists_only_excel_processes_that_still_break_a_rule()
    {
        var (repo, q, _) = Seeded();
        var legacy = new IbProcess { ValueStreamId = 4, FolioNumber = 777, IsLegacy = true, LegacySource = "VS4!row 9", CreatedBy = "MIGRATION" };
        legacy.AddPallet(new PalletReturn { ReturnedMaterial = "bad", QtyImported = 0 });
        repo.AppendLegacy(new[] { legacy });
        var rows = q.Page(new ProcessFilter(OnlyFindings: true), ProcessSort.Default, 0, 50);
        Assert.Equal("VS4-00777", Assert.Single(rows).FolioCode);
        Assert.True(rows[0].OpenFindings > 0);
        Assert.True(q.AnyLegacy());
    }

    [Fact]
    public void The_breakdown_and_the_export_come_from_the_same_filter()
    {
        var (_, q, _) = Seeded();
        var f = new ProcessFilter(ValueStreamId: 1);
        var page = q.Page(f, ProcessSort.Default, 0, 5);
        var pallets = q.PalletsOf(page.Select(r => r.Id).ToList());
        Assert.Equal(page.Sum(r => r.PalletCount), pallets.Count);
        Assert.All(pallets, x => Assert.StartsWith("VS1-", x.FolioCode));
        var export = q.Load(f, ProcessSort.Default);
        Assert.Equal(q.Totals(f).Count, export.Count);
        Assert.Equal(page.Select(r => r.Id), export.Take(5).Select(p => p.Id));                 // same order as the inbox
    }

    [Fact]
    public void Several_part_numbers_show_the_first_and_how_many_more()
    {
        var s = new ProcessSummary(1, 4, 1, "VS4-00001", WorkflowStatus.Imported, 3, 30, "0203.306.644-250", 3, "49398082", 1, 0, 0, 0,
            false, null, null, null, null, null, null, null, null, null, false, 0, null);
        Assert.Equal("0203.306.644-250 (+2)", s.MaterialText);
        Assert.Equal("49398082", s.PoText);
    }
}
