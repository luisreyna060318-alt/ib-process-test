using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>The alerts screen reads one page at a time (Oracle: SQL that follows ReportingService.Alerts).</summary>
public class AlertPagingTests
{
    private static ReportingService Seeded()
    {
        var repo = new InMemoryProcessRepository();
        var clock = new SystemClock();
        SampleData.Seed(repo, new InMemoryAuditTrail(), new UserDirectory((string?)null, Array.Empty<string>(), includeDemoUsers: true), clock);
        return new ReportingService(new InMemoryProcessQueries(repo, clock), clock);
    }

    [Fact]
    public void Pages_put_together_are_every_alert_of_the_filter_in_the_screen_order()
    {
        var reporting = Seeded();
        var all = reporting.Alerts();
        Assert.NotEmpty(all);
        var handledOne = all[0];
        bool Handled(int id, AlertType t) => id == handledOne.PalletId && t == handledOne.Type;

        foreach (var f in new[] { new AlertFilter(true), new AlertFilter() }.Concat(AlertFilter.Areas.Select(a => new AlertFilter(false, a))))
        {
            var expected = AlertTexts.Ordered(all.Where(a => f.Keeps(a, Handled(a.PalletId, a.Type)))).ToList();
            var pages = new List<Alert>();
            for (var skip = 0; skip <= expected.Count; skip += 7) pages.AddRange(reporting.AlertPage(f, Handled, skip, 7));
            Assert.Equal(expected, pages);
            Assert.Equal(expected.Count, reporting.AlertTotals(f, Handled).Shown);
        }
        var totals = reporting.AlertTotals(new AlertFilter(), Handled);
        Assert.Equal(1, totals.Handled);
        Assert.Equal(all.Count(a => a.Severity == Severity.Error && !Handled(a.PalletId, a.Type)), totals.Critical);
    }

    [Fact]
    public void Area_filter_keeps_the_alerts_where_that_area_is_responsible()
    {
        var reporting = Seeded();
        var osd = reporting.AlertPage(new AlertFilter(false, "OSD"), (_, _) => false, 0, 1000);
        Assert.All(osd, a => Assert.Contains("OSD", a.Owner.Split(" + ")));
        Assert.Equal(reporting.Alerts().Count(a => a.Owner.Split(" + ").Contains("OSD")), osd.Count);
    }
}
