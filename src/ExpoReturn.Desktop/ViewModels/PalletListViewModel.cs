using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Microsoft.Win32;

namespace ExpoReturn.Desktop.ViewModels;

/// <summary>Inbox row: one IB PROCESS as the database returns it (IB_PROCESS_INBOX_V), with its per-pallet breakdown.</summary>
public sealed partial class ProcessRow : ObservableObject
{
    public ProcessRow(ProcessSummary s, IReadOnlyList<PalletLine> pallets)
    {
        Summary = s;
        Pallets = pallets;
    }
    public ProcessSummary Summary { get; }
    public IReadOnlyList<PalletLine> Pallets { get; }
    public int Id => Summary.Id;
    public string FolioCode => Summary.FolioCode;
    public string ValueStream => $"VS{Summary.ValueStreamId}";
    public WorkflowStatus Status => Summary.Status;
    public string StatusName => Summary.Status.DisplayName();
    public int PalletCount => Summary.PalletCount;
    public string Material => Summary.MaterialText;
    public string PoNumbers => Summary.PoText;
    public int Qty => Summary.Qty;
    public decimal SapUsdTotal => Summary.SapUsdTotal;
    public decimal Balance => Summary.Balance;
    public int? DaysInJuP1 => Summary.DaysInJuP1;
    public DateTime? DueDate => Summary.NearestDueDate;
    public int? DaysToLegalDue => Summary.DaysToDue;
    public string? Qmm => Summary.QmmResponsible;
    public string? Requestor => Summary.RequestorName;
    public string? Department => Summary.RequestingDepartment;
    public string? Invoice => Summary.ImportInvoice;
    public string? DqText => Summary.DataQuality;
    public bool IsDutyRisk => Summary.IsDutyRisk;
    /// <summary>The per-pallet breakdown is shown under the row.</summary>
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>One pallet in the inbox breakdown (IB_PROCESS_PALLETS_V).</summary>
public sealed class PalletLine
{
    public PalletLine(PalletSummary p)
    {
        Pallet = p;
        Category = Catalogs.FindCategory(p.StatusCategoryCode)?.Description ?? p.StatusCategoryCode;
    }
    public PalletSummary Pallet { get; }
    public int Id => Pallet.Id;
    public int LineNo => Pallet.LineNo;
    public string FolioCode => Pallet.FolioCode;
    public WorkflowStatus Status => Pallet.Status;
    public string StatusName => Pallet.Status.DisplayName();
    public string? Material => Pallet.Material;
    public int Qty => Pallet.Qty;
    public string HuOrBatch => Pallet.HuOrBatch;
    public string? Po => Pallet.PoNumber;
    public decimal Balance => Pallet.Balance;
    public int? DaysToLegalDue => Pallet.DaysToDue;
    public string? Category { get; }
    public string? CustomsExport => Pallet.CustomsExport;
}

/// <summary>One button of the pager: a page number, or "…" (Number = null).</summary>
public sealed record PageButton(int? Number, bool IsCurrent)
{
    public string Text => Number?.ToString("N0") ?? "…";
    public bool IsPage => Number is not null && !IsCurrent;

    /// <summary>1 … c-2 c-1 [c] c+1 c+2 … N</summary>
    public static IReadOnlyList<PageButton> Build(int current, int count)
    {
        var list = new List<PageButton>();
        if (count <= 1) return list;
        var pages = new SortedSet<int> { 1, count };
        for (var p = current - 2; p <= current + 2; p++) if (p >= 1 && p <= count) pages.Add(p);
        var previous = 0;
        foreach (var p in pages)
        {
            if (p - previous == 2) list.Add(new PageButton(previous + 1, false));
            else if (p - previous > 2) list.Add(new PageButton(null, false));
            list.Add(new PageButton(p, p == current));
            previous = p;
        }
        return list;
    }
}

/// <summary>Filtro inicial al llegar desde otra pantalla.</summary>
public sealed record ListFilter(int? ValueStreamId = null, WorkflowStatus? Status = null, bool OnlyOpen = false);

public sealed partial class PalletListViewModel : PageViewModel
{
    private readonly Action<int> _open;
    private readonly DispatcherTimer _searchDelay;
    private bool _loadingFilters;

    public PalletListViewModel(AppServices services, UserContext user, Action<int> open, ListFilter? initial = null) : base(services, user)
    {
        _open = open;
        ValueStreamOptions = new[] { new LookupItem(null, "All VS") }
            .Concat(Catalogs.ValueStreams.Select(v => new LookupItem(v.Id, v.Code))).ToList();
        StatusOptions = new[] { new LookupItem("open", "Open"), new LookupItem(null, "All") }
            .Concat(Enum.GetValues<WorkflowStatus>().Select(s => new LookupItem(s, s.DisplayName()))).ToList();
        _selectedValueStream = ValueStreamOptions[0];
        _selectedStatus = StatusOptions[0];
        _isTableShown = s_tableShown;
        _sort = s_sort;
        // Typing in the search asks the database once the user pauses, not on every key.
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); FiltersChanged(); };
        var page = s_page;

        if (initial is not null) ApplyInitial(initial);
        else LoadPreferences();
        Refresh();
        if (initial is null && IsTableShown && page > 1) GoToPage(page);      // back from a record: same page as before
    }

    public override string Title => "IB PROCESS inbox";

    // ---------- Table: shown on demand, 50 records per page, read page by page from the database ----------
    public const int PageSize = 50;
    private static bool s_tableShown;          // remembered while the application is open
    private static int s_page = 1;
    private static ProcessSort s_sort = ProcessSort.Default;
    private ProcessSort _sort;
    private ProcessFilter _filter = ProcessFilter.All;

    /// <summary>Records of the current page (what the grid shows).</summary>
    public ObservableCollection<ProcessRow> PageRows { get; } = new();
    public ObservableCollection<PageButton> PageButtons { get; } = new();
    [ObservableProperty] private bool _isTableShown;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _pageSummary = "";
    public bool HasSeveralPages => PageCount > 1;
    public bool HasRecords => KpiCount > 0;
    public string ShowButtonText => KpiCount == 0 ? "No records match the filters" : $"Show {KpiCount:N0} record{(KpiCount == 1 ? "" : "s")}";
    /// <summary>Sort column chosen (for the grid header arrow when coming back).</summary>
    public ProcessSort Sort => _sort;

    partial void OnIsTableShownChanged(bool value) { s_tableShown = value; if (value) ShowPage(); else ClearPage(); }
    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(HasSeveralPages));
    partial void OnKpiCountChanged(int value) { OnPropertyChanged(nameof(HasRecords)); OnPropertyChanged(nameof(ShowButtonText)); }

    [RelayCommand] private void ShowTable() { if (HasRecords) IsTableShown = true; }
    [RelayCommand] private void HideTable() => IsTableShown = false;
    [RelayCommand] private void FirstPage() => GoToPage(1);
    [RelayCommand] private void PreviousPage() => GoToPage(Page - 1);
    [RelayCommand] private void NextPage() => GoToPage(Page + 1);
    [RelayCommand] private void LastPage() => GoToPage(PageCount);
    [RelayCommand] private void GoToPage(int? page)
    {
        if (page is not { } target) return;                        // the "…" of the pager
        Page = Math.Clamp(target, 1, Math.Max(1, PageCount));
        s_page = Page;
        ShowPage();
    }

    /// <summary>Sorts ALL the filtered records by a column (the database orders, then the page is read). Returns true = descending.</summary>
    public bool SortBy(string member)
    {
        var descending = _sort.Member == member && !_sort.Descending;
        _sort = s_sort = new ProcessSort(member, descending);
        Page = 1;
        s_page = 1;
        ShowPage();
        return descending;
    }

    /// <summary>Reads the current page and the pallets of its processes (two queries of at most 50 processes).</summary>
    private void ShowPage()
    {
        PageCount = Math.Max(1, (int)Math.Ceiling(KpiCount / (double)PageSize));
        if (Page > PageCount) { Page = PageCount; s_page = Page; }
        ClearPage();
        if (!IsTableShown) return;                                  // nothing is read until the user asks for it
        var first = (Page - 1) * PageSize;
        if (!Try(() =>
            {
                var rows = Services.Queries.Page(_filter, _sort, first, PageSize);
                var pallets = Services.Queries.PalletsOf(rows.Select(r => r.Id).ToList()).ToLookup(p => p.ProcessId);
                foreach (var r in rows) PageRows.Add(new ProcessRow(r, pallets[r.Id].Select(p => new PalletLine(p)).ToList()));
            })) return;
        PageSummary = KpiCount == 0 ? "No records" : $"Records {first + 1:N0}–{first + PageRows.Count:N0} of {KpiCount:N0}";
        foreach (var b in PageButton.Build(Page, PageCount)) PageButtons.Add(b);
    }

    private void ClearPage()
    {
        PageRows.Clear();
        PageButtons.Clear();
    }

    /// <summary>A query that fails (network, database) shows the reason instead of closing the screen.</summary>
    private bool Try(Action query)
    {
        try { query(); return true; }
        catch (Exception ex)
        {
            Message = $"Could not read from the database: {ex.Message}. Press Reload to try again.";
            return false;
        }
    }

    public IReadOnlyList<LookupItem> ValueStreamOptions { get; }
    public IReadOnlyList<LookupItem> StatusOptions { get; }
    /// <summary>The "Data quality" column and the "To homogenize" filter only when there are Excel 2.0 records.</summary>
    [ObservableProperty] private bool _hasLegacy;

    [ObservableProperty] private LookupItem _selectedValueStream;
    [ObservableProperty] private LookupItem _selectedStatus;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _onlyDutyRisk;
    /// <summary>Homogenization: only records imported from the Excel that still break a validation rule.</summary>
    [ObservableProperty] private bool _onlyDataFindings;
    [ObservableProperty] private ProcessRow? _selectedRow;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private int _kpiCount;
    [ObservableProperty] private int _kpiOpen;
    [ObservableProperty] private int _kpiDutyRisk;
    [ObservableProperty] private decimal _kpiBalance;

    partial void OnSelectedValueStreamChanged(LookupItem value) => FiltersChanged();
    partial void OnSelectedStatusChanged(LookupItem value) => FiltersChanged();
    partial void OnSearchTextChanged(string value)
    {
        if (_loadingFilters) return;
        _searchDelay.Stop();
        _searchDelay.Start();
    }
    partial void OnOnlyDutyRiskChanged(bool value) => FiltersChanged();
    partial void OnOnlyDataFindingsChanged(bool value) => FiltersChanged();

    protected override void UserChanged()
    {
        LoadPreferences();
        ApplyFilter();
    }

    /// <summary>Reload: what others saved since the page was read.</summary>
    public override void Refresh()
    {
        Message = null;
        Try(() => HasLegacy = Services.Queries.AnyLegacy());
        ApplyFilter(keepPage: true);
    }

    private void FiltersChanged()
    {
        if (_loadingFilters) return;
        ApplyFilter();
        Services.Preferences.SetListFilter(User.NtUser, new ListFilterPreference
        {
            ValueStreamId = SelectedValueStream?.Value as int?,
            Status = SelectedStatus?.Value?.ToString(),
            Search = SearchText,
            OnlyDutyRisk = OnlyDutyRisk,
        });
    }

    private void LoadPreferences()
    {
        var pref = Services.Preferences.GetListFilter(User.NtUser);
        _loadingFilters = true;
        try
        {
            SelectedValueStream = ValueStreamOptions.FirstOrDefault(o => Equals(o.Value, pref?.ValueStreamId)) ?? ValueStreamOptions[0];
            SelectedStatus = StatusOptions.FirstOrDefault(o => o.Value?.ToString() == pref?.Status) ?? StatusOptions[0];
            SearchText = pref?.Search ?? "";
            OnlyDutyRisk = pref?.OnlyDutyRisk ?? false;
        }
        finally { _loadingFilters = false; }
    }

    private void ApplyInitial(ListFilter f)
    {
        _loadingFilters = true;
        try
        {
            SelectedValueStream = ValueStreamOptions.FirstOrDefault(o => Equals(o.Value, f.ValueStreamId)) ?? ValueStreamOptions[0];
            SelectedStatus = f.Status is { } s ? StatusOptions.First(o => Equals(o.Value, s))
                           : f.OnlyOpen ? StatusOptions[0] : StatusOptions[1];
            SearchText = "";
            OnlyDutyRisk = false;
        }
        finally { _loadingFilters = false; }
    }

    private ProcessFilter CurrentFilter() => new(
        SelectedValueStream?.Value as int?,
        SelectedStatus?.Value as WorkflowStatus?,
        OnlyOpen: Equals(SelectedStatus?.Value, "open"),
        OnlyDutyRisk: OnlyDutyRisk,
        OnlyFindings: OnlyDataFindings,
        Search: SearchText);

    /// <summary>The indicators always follow the filters (one COUNT/SUM query); the page is read only if the table is shown.</summary>
    private void ApplyFilter(bool keepPage = false)
    {
        _filter = CurrentFilter();
        if (!keepPage) { Page = 1; s_page = 1; }               // a new filter starts at the first page
        if (!Try(() =>
            {
                var t = Services.Queries.Totals(_filter);
                KpiCount = t.Count;
                KpiOpen = t.Open;
                KpiDutyRisk = t.DutyRisk;
                KpiBalance = t.Balance;
            })) return;
        ShowPage();
    }

    private string FilterDescription =>
        $"{SelectedValueStream?.Label} · {SelectedStatus?.Label}" +
        (string.IsNullOrWhiteSpace(SearchText) ? "" : $" · \"{SearchText}\"") +
        (OnlyDutyRisk ? " · duty risk only" : "") + (OnlyDataFindings ? " · to homogenize" : "");

    [RelayCommand]
    private void Open(ProcessRow? row)
    {
        row ??= SelectedRow;
        if (row is not null) _open(row.Id);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _loadingFilters = true;
        try
        {
            SelectedValueStream = ValueStreamOptions[0];
            SelectedStatus = StatusOptions[1];
            SearchText = "";
            OnlyDutyRisk = false;
            OnlyDataFindings = false;
        }
        finally { _loadingFilters = false; }
        FiltersChanged();
    }

    /// <summary>Exports every process of the filter (read from the database at that moment) to a read-only Excel file.</summary>
    [RelayCommand]
    private void Export()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export inbox (read-only)",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"IB_PROCESS_inbox_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var processes = Services.Queries.Load(CurrentFilter(), _sort);
            var pallets = processes.SelectMany(p => p.Pallets).ToList();
            ExcelExporter.ExportPallets(pallets, dlg.FileName, Services.Clock.Today, FilterDescription);
            Message = $"{processes.Count} IB processes ({pallets.Count} pallets) exported to {System.IO.Path.GetFileName(dlg.FileName)} (protected, read-only sheet).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export:\n{ex.Message}\n\nIf the file is open in Excel, close it.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
