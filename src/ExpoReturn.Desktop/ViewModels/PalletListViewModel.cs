using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Desktop.ViewModels;

/// <summary>Renglón de la bandeja (proyección de lectura).</summary>
public sealed class PalletRow
{
    public PalletRow(PalletReturn p, DateTime today)
    {
        Pallet = p;
        DaysInJuP1 = p.ImportDateElpJup1 is null ? null : p.DaysInJuP1(today);
        DaysToLegalDue = p.LegalDueDate is null || p.Status.IsClosed() ? null : (int)(p.LegalDueDate.Value - today).TotalDays;
        Category = Catalogs.FindCategory(p.StatusCategoryCode)?.Description ?? p.StatusCategoryCode;
    }
    public PalletReturn Pallet { get; }
    public int Id => Pallet.Id;
    public string FolioCode => Pallet.FolioCode;
    public string ValueStream => $"VS{Pallet.ValueStreamId}";
    public WorkflowStatus Status => Pallet.Status;
    public string StatusName => Pallet.StatusName;
    public string? Material => Pallet.ReturnedMaterial;
    public int Qty => Pallet.QtyImported;
    public decimal SapUsdTotal => Pallet.SapUsdTotal;
    public decimal Balance => Pallet.Financials.BalanceGeneralUsd;
    public int? DaysInJuP1 { get; }
    public int? DaysToLegalDue { get; }
    public string? Qmm => Pallet.QmmResponsible;
    public string? Category { get; }
    public string? Invoice => Pallet.ImportInvoice;
    public string? HuOrBatch => Pallet.OriginalHU ?? Pallet.OriginalBatch;
    public int DqCount => Pallet.DataQualityFlags.Count;
    public string DqText => Pallet.DataQualityText;
    public bool IsDutyRisk => Pallet.IsDutyRisk;
    /// <summary>Orden por defecto: abiertos más próximos a vencer primero; cerrados al final.</summary>
    public int SortKey => Status.IsClosed() ? 100000 : DaysToLegalDue ?? 50000;
}

public sealed partial class PalletListViewModel : PageViewModel
{
    private readonly Action<int> _open;
    private readonly ObservableCollection<PalletRow> _rows = new();

    public PalletListViewModel(AppServices services, UserContext user, Action<int> open) : base(services, user)
    {
        _open = open;
        ValueStreamOptions = new[] { new LookupItem(null, "Todos los VS") }
            .Concat(Catalogs.ValueStreams.Select(v => new LookupItem(v.Id, v.Code))).ToList();
        StatusOptions = new[] { new LookupItem("open", "Abiertos"), new LookupItem(null, "Todos") }
            .Concat(Enum.GetValues<WorkflowStatus>().Select(s => new LookupItem(s, s.DisplayName()))).ToList();
        _selectedValueStream = ValueStreamOptions[0];
        _selectedStatus = StatusOptions[0];
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Rows.Filter = Filter;
        Refresh();
    }

    public override string Title => "Bandeja de retornos";
    public ICollectionView Rows { get; }
    public IReadOnlyList<LookupItem> ValueStreamOptions { get; }
    public IReadOnlyList<LookupItem> StatusOptions { get; }
    public bool HasLegacy => _rows.Any(r => r.Pallet.IsLegacy);

    [ObservableProperty] private LookupItem _selectedValueStream;
    [ObservableProperty] private LookupItem _selectedStatus;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _onlyDutyRisk;
    [ObservableProperty] private PalletRow? _selectedRow;

    [ObservableProperty] private int _kpiCount;
    [ObservableProperty] private int _kpiOpen;
    [ObservableProperty] private int _kpiDutyRisk;
    [ObservableProperty] private decimal _kpiBalance;
    [ObservableProperty] private decimal _kpiDutyExposure;

    partial void OnSelectedValueStreamChanged(LookupItem value) => ApplyFilter();
    partial void OnSelectedStatusChanged(LookupItem value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOnlyDutyRiskChanged(bool value) => ApplyFilter();

    public override void Refresh()
    {
        _rows.Clear();
        var today = Services.Clock.Today;
        // Orden por defecto en los datos (no en SortDescriptions: el DataGrid las limpia al recrear la vista).
        foreach (var r in Services.Repository.GetAll().Select(p => new PalletRow(p, today))
                     .OrderBy(r => r.SortKey).ThenBy(r => r.FolioCode))
            _rows.Add(r);
        OnPropertyChanged(nameof(HasLegacy));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<PalletRow>().ToList();
        KpiCount = visible.Count;
        KpiOpen = visible.Count(r => !r.Status.IsClosed());
        KpiDutyRisk = visible.Count(r => r.IsDutyRisk);
        KpiBalance = visible.Sum(r => r.Balance);
        KpiDutyExposure = visible.Sum(r => r.Pallet.Financials.DutyExposureUsd);
    }

    private bool Filter(object o)
    {
        if (o is not PalletRow r) return false;
        if (SelectedValueStream?.Value is int vs && r.Pallet.ValueStreamId != vs) return false;
        switch (SelectedStatus?.Value)
        {
            case "open" when r.Status.IsClosed(): return false;
            case WorkflowStatus s when r.Status != s: return false;
        }
        if (OnlyDutyRisk && !r.IsDutyRisk) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var t = SearchText.Trim();
            bool Has(string? s) => s?.Contains(t, StringComparison.OrdinalIgnoreCase) == true;
            if (!(Has(r.FolioCode) || Has(r.Material) || Has(r.HuOrBatch) || Has(r.Invoice) || Has(r.Qmm) || Has(r.Pallet.CustomsExportReference)))
                return false;
        }
        return true;
    }

    [RelayCommand]
    private void Open(PalletRow? row)
    {
        row ??= SelectedRow;
        if (row is not null) _open(row.Id);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedValueStream = ValueStreamOptions[0];
        SelectedStatus = StatusOptions[1];
        SearchText = "";
        OnlyDutyRisk = false;
    }
}
