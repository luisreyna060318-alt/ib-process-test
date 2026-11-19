using ExpoReturn.Domain;

namespace ExpoReturn.Application;

// =====================================================================
// Read side: the inbox, Balance / Duties and Alerts ask the store for one page or one total at a time.
// Oracle answers with SQL on the views of 05_views.sql (OFFSET … FETCH); the in-memory store answers with the same rules
// in C# (tests). Everything that depends on the date (due date, duty risk, status) is calculated at query time.
// =====================================================================

/// <summary>Filters of the inbox (all optional; combined with AND).</summary>
public sealed record ProcessFilter(int? ValueStreamId = null, WorkflowStatus? Status = null, bool OnlyOpen = false,
                                   bool OnlyDutyRisk = false, bool OnlyFindings = false, string? Search = null, string? MineOf = null,
                                   IReadOnlyList<AreaScope>? PendingFor = null)
{
    public static ProcessFilter All { get; } = new();

    /// <summary>
    /// "My IB PROCESS": the processes the user created, is QMM Responsible of, or is the Requestor of (registered users;
    /// historical Excel rows only carry names, so they are nobody's). Same rule as the SQL of the inbox.
    /// </summary>
    public static bool IsMine(IbProcess p, string ntUser) =>
        string.Equals(p.CreatedBy, ntUser, StringComparison.OrdinalIgnoreCase)
        || string.Equals(p.QmmResponsibleNtUser, ntUser, StringComparison.OrdinalIgnoreCase)
        || string.Equals(p.RequestorNtUser, ntUser, StringComparison.OrdinalIgnoreCase);

    /// <summary>Search text trimmed, or null.</summary>
    public string? SearchText => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    /// <summary>Same rule as the SQL of the inbox (folio, header texts, and any pallet's part number, HU, batch, PO,
    /// customs export or pallet folio), case-insensitive.</summary>
    public static bool Matches(IbProcess p, string text)
    {
        bool Has(string? s) => s?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
        return Has(p.FolioCode) || Has(p.ImportInvoice) || Has(p.QmmResponsible) || Has(p.RequestorName) || Has(p.DeliveryNumber)
               || p.Pallets.Any(x => Has(x.ReturnedMaterial) || Has(x.OriginalHU) || Has(x.OriginalBatch) || Has(x.ReturnPONumber)
                                     || Has(x.CustomsExportReference) || Has(x.FolioCode));
    }
}

/// <summary>Order of the inbox: a column of <see cref="Members"/> (null = default order) and its direction.</summary>
public sealed record ProcessSort(string? Member = null, bool Descending = false)
{
    public static ProcessSort Default { get; } = new();

    /// <summary>Columns the inbox can be sorted by (names of the row properties; anything else = default order).</summary>
    public static readonly IReadOnlyList<string> Members = new[]
    {
        "FolioCode", "ValueStream", "Status", "PalletCount", "Qty", "Material", "PoNumbers", "SapUsdTotal", "Balance",
        "DaysInJuP1", "DueDate", "DaysToLegalDue", "Qmm", "Requestor", "Department", "Invoice", "DqText",
    };

    public bool IsDefault => Member is null || !Members.Contains(Member);
}

/// <summary>One inbox row: an IB PROCESS with the totals of its pallets (what IB_PROCESS_INBOX_V returns).</summary>
public sealed record ProcessSummary(
    int Id, int ValueStreamId, int FolioNumber, string FolioCode, WorkflowStatus Status, int PalletCount, int Qty,
    string? FirstPartNumber, int PartNumberCount, string? FirstPoNumber, int PoNumberCount, decimal SapUsdTotal, decimal Balance,
    decimal DutiesToPayUsd, bool IsDutyRisk, DateTime? CrossingDate, int? DaysInJuP1, DateTime? NearestDueDate, int? DaysToDue,
    string? QmmResponsible, string? RequestorName, string? RequestingDepartment, string? ImportInvoice, string? DeliveryNumber,
    bool IsLegacy, int OpenFindings, string? DataQuality)
{
    public bool IsClosed => Status.IsClosed();
    /// <summary>"0203.306.644-250 (+2)" when the pallets have several part numbers.</summary>
    public string MaterialText => Several(FirstPartNumber, PartNumberCount);
    public string PoText => Several(FirstPoNumber, PoNumberCount);
    private static string Several(string? first, int count) => first is null ? "" : count > 1 ? $"{first} (+{count - 1})" : first;
}

/// <summary>One pallet of the inbox breakdown (what IB_PROCESS_PALLETS_V returns).</summary>
public sealed record PalletSummary(
    int Id, int ProcessId, int LineNo, string FolioCode, WorkflowStatus Status, string? Material, int Qty, string? OriginalHU,
    string? OriginalBatch, string? PoNumber, decimal Balance, DateTime? LegalDueDate, int? DaysToDue, string? StatusCategoryCode,
    string? CustomsExport)
{
    public string HuOrBatch => !string.IsNullOrWhiteSpace(OriginalHU) ? OriginalHU! : !string.IsNullOrWhiteSpace(OriginalBatch) ? OriginalBatch! : "—";
}

/// <summary>Indicators of the inbox for a filter (every matching process, not only the page).</summary>
public sealed record ProcessTotals(int Count, int Open, int DutyRisk, decimal Balance)
{
    public static ProcessTotals Zero { get; } = new(0, 0, 0, 0m);
}

public interface IProcessQueries
{
    ProcessTotals Totals(ProcessFilter filter);
    /// <summary>One page: <paramref name="skip"/> rows skipped, at most <paramref name="take"/> returned, in a stable order.</summary>
    IReadOnlyList<ProcessSummary> Page(ProcessFilter filter, ProcessSort sort, int skip, int take);
    /// <summary>Pallets of the given processes (the breakdown of a page), by process and line.</summary>
    IReadOnlyList<PalletSummary> PalletsOf(IReadOnlyCollection<int> processIds);
    /// <summary>Whole processes of a filter (export to Excel), in the order asked.</summary>
    IReadOnlyList<IbProcess> Load(ProcessFilter filter, ProcessSort sort);
    /// <summary>There are Excel 2.0 processes (the inbox shows the data quality column and the To homogenize filter).</summary>
    bool AnyLegacy();
    IReadOnlyList<BalanceRow> Balance(int? fiscalYear);
    /// <summary>Processes per status (the same status the inbox filters by).</summary>
    IReadOnlyList<StatusCount> StatusDistribution();
    IReadOnlyList<int> FiscalYears();
    /// <summary>Open pallets (not closed), each with its process: what the alerts are calculated on.</summary>
    IReadOnlyList<PalletReturn> OpenPallets();
}

/// <summary>The rules of the views in C#: the in-memory store uses them, and the tests compare both.</summary>
public static class ProcessSummaries
{
    /// <summary>Row of an evaluated process (its pallets already calculated with <paramref name="today"/>).</summary>
    public static ProcessSummary From(IbProcess p, DateTime today)
    {
        var due = p.LegalDueDate;
        var parts = p.Pallets.Select(x => x.ReturnedMaterial).Where(s => s is not null).Distinct().ToList();
        var pos = p.Pallets.Select(x => x.ReturnPONumber).Where(s => s is not null).Distinct().ToList();
        return new ProcessSummary(p.Id, p.ValueStreamId, p.FolioNumber, p.FolioCode, p.Status, p.PalletCount, p.QtyImported,
            parts.OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault(), parts.Count,
            pos.OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault(), pos.Count,
            p.SapUsdTotal, p.BalanceGeneralUsd, p.DutiesToPayUsd, p.IsDutyRisk, p.ImportDateElpJup1,
            p.ImportDateElpJup1 is { } c ? (int)(today.Date - c.Date).TotalDays : null,
            due, due is null || p.Status.IsClosed() ? null : (int)(due.Value - today.Date).TotalDays,
            p.QmmResponsible, p.RequestorName, p.RequestingDepartment, p.ImportInvoice, p.DeliveryNumber, p.IsLegacy, p.OpenFindings,
            p.Pallets.Select(x => x.DataQualityText).Where(s => s.Length > 0).OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault());
    }

    public static PalletSummary From(PalletReturn x, DateTime today) =>
        new(x.Id, x.Process.Id, x.LineNo, x.FolioCode, x.Status, x.ReturnedMaterial, x.QtyImported, x.OriginalHU, x.OriginalBatch,
            x.ReturnPONumber, x.Financials.BalanceGeneralUsd, x.LegalDueDate,
            x.LegalDueDate is null || x.Status.IsClosed() ? null : (int)(x.LegalDueDate.Value - today.Date).TotalDays,
            x.StatusCategoryCode, x.CustomsExportReference);

    public static bool Keep(ProcessSummary s, IbProcess p, ProcessFilter f)
    {
        if (f.ValueStreamId is { } vs && s.ValueStreamId != vs) return false;
        if (f.Status is { } st && s.Status != st) return false;
        if (f.OnlyOpen && s.IsClosed) return false;
        if (f.OnlyDutyRisk && !s.IsDutyRisk) return false;
        if (f.OnlyFindings && !(s.IsLegacy && s.OpenFindings > 0)) return false;
        if (!string.IsNullOrWhiteSpace(f.MineOf) && !ProcessFilter.IsMine(p, f.MineOf.Trim())) return false;
        if (f.PendingFor is { } scopes && !PendingWork.Matches(p, scopes)) return false;
        return f.SearchText is not { } t || ProcessFilter.Matches(p, t);
    }

    /// <summary>
    /// Order of the SQL: the column asked (empty values last), then VS and folio (stable pages). Default: open processes
    /// closest to their IMMEX due date first, closed last.
    /// </summary>
    public static IEnumerable<ProcessSummary> Sort(IEnumerable<ProcessSummary> rows, ProcessSort sort)
    {
        IOrderedEnumerable<ProcessSummary> ordered;
        if (sort.IsDefault)
            ordered = rows.OrderBy(r => r.IsClosed).ThenBy(r => r.DaysToDue is null).ThenBy(r => r.DaysToDue);
        else
        {
            Func<ProcessSummary, IComparable?> key = Key(sort.Member!);
            ordered = rows.OrderBy(r => key(r) is null);
            ordered = sort.Descending ? ordered.ThenByDescending(key, Comparer) : ordered.ThenBy(key, Comparer);
        }
        return ordered.ThenBy(r => r.ValueStreamId).ThenBy(r => r.FolioNumber).ThenBy(r => r.Id);
    }

    private static readonly Comparer<IComparable?> Comparer = Comparer<IComparable?>.Create((a, b) =>
        a is null ? (b is null ? 0 : 1) : b is null ? -1
        : a is string sa && b is string sb ? string.CompareOrdinal(sa, sb) : a.CompareTo(b));

    private static Func<ProcessSummary, IComparable?> Key(string member) => member switch
    {
        "FolioCode" => r => r.ValueStreamId * 1_000_000_000L + r.FolioNumber,
        "ValueStream" => r => r.ValueStreamId,
        "Status" => r => (int)r.Status,
        "PalletCount" => r => r.PalletCount,
        "Qty" => r => r.Qty,
        "Material" => r => r.FirstPartNumber,
        "PoNumbers" => r => r.FirstPoNumber,
        "SapUsdTotal" => r => r.SapUsdTotal,
        "Balance" => r => r.Balance,
        "DaysInJuP1" => r => r.DaysInJuP1,
        "DueDate" => r => r.NearestDueDate,
        "DaysToLegalDue" => r => r.DaysToDue,
        "Qmm" => r => r.QmmResponsible,
        "Requestor" => r => r.RequestorName,
        "Department" => r => r.RequestingDepartment,
        "Invoice" => r => r.ImportInvoice,
        "DqText" => r => r.DataQuality,
        _ => r => null,
    };

    public static ProcessTotals Totals(IReadOnlyCollection<ProcessSummary> rows) =>
        new(rows.Count, rows.Count(r => !r.IsClosed), rows.Count(r => r.IsDutyRisk), rows.Sum(r => r.Balance));

    /// <summary>Balance / Duties per VS of the pallets with crossing (fiscal year = year of the crossing), plus the total.</summary>
    public static IReadOnlyList<BalanceRow> Balance(IEnumerable<PalletReturn> pallets, int? fiscalYear)
    {
        var list = pallets.Where(p => p.ImportDateElpJup1 is not null && (fiscalYear is null || p.ImportDateElpJup1!.Value.Year == fiscalYear)).ToList();
        var rows = list.GroupBy(p => p.ValueStreamId).OrderBy(g => g.Key).Select(g => Row($"VS{g.Key}", g.ToList())).ToList();
        if (rows.Count > 0) rows.Add(Row("Total", list));
        return rows;
    }

    private static BalanceRow Row(string vs, List<PalletReturn> list)
    {
        var total = list.Sum(p => p.SapUsdTotal);
        var inv = list.Sum(p => p.Financials.MfoInventoryUsd);
        var hse = list.Sum(p => p.Financials.HseScrapUsd);
        var fail = list.Sum(p => p.Financials.MfoLowFailureUsd);
        return new BalanceRow(vs, list.Count, list.Count(p => !p.Status.IsClosed()), list.Count(p => p.IsDutyRisk),
            total, inv, hse, fail, total - inv - hse - fail, inv + hse + fail, list.Sum(p => p.Financials.DutiesToPayUsd));
    }
}
