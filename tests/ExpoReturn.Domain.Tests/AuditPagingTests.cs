using ExpoReturn.Application;
using ExpoReturn.Infrastructure.InMemory;
using Xunit;

namespace ExpoReturn.Domain.Tests;

/// <summary>The audit log screen reads one page at a time with the filters applied by the store (Oracle: SQL).</summary>
public class AuditPagingTests
{
    private static InMemoryAuditTrail Log()
    {
        var log = new InMemoryAuditTrail();
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        log.Record(Enumerable.Range(0, 130).Select(i => new AuditEntry(
            i % 3, $"VS{(i % 2 == 0 ? 7 : 1)}-{i:00000}", i % 4 == 0 ? "ImportInvoice" : "DeliveryNumber", null, $"v{i}",
            i % 5 == 0 ? @"US\ABC1" : @"US\XYZ2", start.AddHours(i * 6), null)));
        return log;
    }

    [Fact]
    public void Pages_put_together_are_the_whole_filter_once_most_recent_first()
    {
        var log = Log();
        foreach (var f in new[]
                 {
                     AuditFilter.All, new AuditFilter(Folio: "vs7"), new AuditFilter(NtUser: @"US\ABC1"),
                     new AuditFilter(Field: "ImportInvoice"), new AuditFilter(From: new DateTime(2026, 11, 10), To: new DateTime(2026, 11, 20)),
                     new AuditFilter(Folio: "VS1", NtUser: @"US\XYZ2", Field: "DeliveryNumber"),
                 })
        {
            var expected = log.All().Where(f.Matches).ToList();
            Assert.Equal(expected.Count, log.Count(f));
            var pages = new List<AuditEntry>();
            for (var skip = 0; skip < expected.Count + 50; skip += 50) pages.AddRange(log.Page(f, skip, 50));
            Assert.Equal(expected, pages);
            Assert.True(pages.Zip(pages.Skip(1)).All(p => p.First.ChangedAtUtc >= p.Second.ChangedAtUtc));
        }
    }

    [Fact]
    public void Filter_options_come_from_the_log()
    {
        var log = Log();
        Assert.Equal(new[] { @"US\ABC1", @"US\XYZ2" }, log.Users());
        Assert.Equal(new[] { "DeliveryNumber", "ImportInvoice" }, log.Fields());
    }
}
