using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// The inbox, Balance and Alerts queries over an in-memory store, with the same rules as the Oracle views (tests and the
/// reference the Oracle SQL is compared with). Each query recalculates with today's date, like the views.
/// </summary>
public sealed class InMemoryProcessQueries : IProcessQueries
{
    private readonly IProcessRepository _repo;
    private readonly IClock _clock;
    private readonly CalcParameters _prm;

    public InMemoryProcessQueries(IProcessRepository repo, IClock clock, CalcParameters? parameters = null)
    {
        _repo = repo; _clock = clock; _prm = parameters ?? CalcParameters.Default;
    }

    private List<IbProcess> Current()
    {
        var today = _clock.Today;
        var all = _repo.GetAll().ToList();
        foreach (var p in all) PalletEvaluator.Apply(p, _prm, today);
        return all;
    }

    private List<(ProcessSummary Row, IbProcess Process)> Matching(ProcessFilter f, ProcessSort sort)
    {
        var today = _clock.Today;
        var rows = Current().Select(p => (Row: ProcessSummaries.From(p, today), Process: p))
            .Where(x => ProcessSummaries.Keep(x.Row, x.Process, f)).ToList();
        var byRow = rows.ToDictionary(x => x.Row, x => x.Process, (IEqualityComparer<ProcessSummary>)ReferenceEqualityComparer.Instance);
        return ProcessSummaries.Sort(rows.Select(x => x.Row), sort).Select(r => (r, byRow[r])).ToList();
    }

    public ProcessTotals Totals(ProcessFilter filter) => ProcessSummaries.Totals(Matching(filter, ProcessSort.Default).Select(x => x.Row).ToList());

    public IReadOnlyList<ProcessSummary> Page(ProcessFilter filter, ProcessSort sort, int skip, int take) =>
        Matching(filter, sort).Skip(Math.Max(0, skip)).Take(Math.Max(0, take)).Select(x => x.Row).ToList();

    public IReadOnlyList<PalletSummary> PalletsOf(IReadOnlyCollection<int> processIds)
    {
        var today = _clock.Today;
        var ids = processIds.ToHashSet();
        return Current().Where(p => ids.Contains(p.Id)).SelectMany(p => p.Pallets)
            .OrderBy(x => x.Process.Id).ThenBy(x => x.LineNo).Select(x => ProcessSummaries.From(x, today)).ToList();
    }

    public IReadOnlyList<IbProcess> Load(ProcessFilter filter, ProcessSort sort) => Matching(filter, sort).Select(x => x.Process).ToList();

    public bool AnyLegacy() => _repo.GetAll().Any(p => p.IsLegacy);

    public IReadOnlyList<BalanceRow> Balance(int? fiscalYear) => ProcessSummaries.Balance(Current().SelectMany(p => p.Pallets), fiscalYear);

    public IReadOnlyList<StatusCount> StatusDistribution() =>
        Current().GroupBy(p => p.Status).OrderBy(g => g.Key).Select(g => new StatusCount(g.Key, g.Count())).ToList();

    public IReadOnlyList<int> FiscalYears() =>
        _repo.GetAll().Where(p => p.ImportDateElpJup1 is not null).Select(p => p.ImportDateElpJup1!.Value.Year).Distinct().OrderBy(y => y).ToList();

    public IReadOnlyList<PalletReturn> OpenPallets() => Current().SelectMany(p => p.Pallets).Where(p => !p.Status.IsClosed()).ToList();
}
