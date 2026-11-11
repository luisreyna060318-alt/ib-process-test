using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Infrastructure.InMemory;
using Microsoft.Win32;

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
        // Imported from the Excel: findings of the current rules (they go away as the record is homogenized).
        OpenFindings = p.IsLegacy ? PalletValidator.Validate(p, today).Count : 0;
    }
    public int OpenFindings { get; }
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

/// <summary>Filtro inicial al llegar desde otra pantalla (UX-B13: clic en Balance).</summary>
public sealed record ListFilter(int? ValueStreamId = null, WorkflowStatus? Status = null, bool OnlyOpen = false);

public sealed partial class PalletListViewModel : PageViewModel
{
    private readonly Action<int> _open;
    private readonly ObservableCollection<PalletRow> _rows = new();
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
        Rows = CollectionViewSource.GetDefaultView(_rows);
        Rows.Filter = Filter;

        if (initial is not null) ApplyInitial(initial);
        else LoadPreferences();
        Refresh();
    }

    public override string Title => "Returns inbox";
    public ICollectionView Rows { get; }
    public IReadOnlyList<LookupItem> ValueStreamOptions { get; }
    public IReadOnlyList<LookupItem> StatusOptions { get; }
    /// <summary>La columna "Calidad de datos" sólo se muestra si hay registros históricos (UX-B07).</summary>
    public bool HasLegacy => _rows.Any(r => r.Pallet.IsLegacy);

    [ObservableProperty] private LookupItem _selectedValueStream;
    [ObservableProperty] private LookupItem _selectedStatus;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _onlyDutyRisk;
    /// <summary>Homogenization: only records imported from the Excel that still break a validation rule.</summary>
    [ObservableProperty] private bool _onlyDataFindings;
    [ObservableProperty] private PalletRow? _selectedRow;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private int _kpiCount;
    [ObservableProperty] private int _kpiOpen;
    [ObservableProperty] private int _kpiDutyRisk;
    [ObservableProperty] private decimal _kpiBalance;

    partial void OnSelectedValueStreamChanged(LookupItem value) => FiltersChanged();
    partial void OnSelectedStatusChanged(LookupItem value) => FiltersChanged();
    partial void OnSearchTextChanged(string value) => FiltersChanged();
    partial void OnOnlyDutyRiskChanged(bool value) => FiltersChanged();
    partial void OnOnlyDataFindingsChanged(bool value) => ApplyFilter();

    protected override void UserChanged()
    {
        LoadPreferences();
        ApplyFilter();
    }

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

    private void FiltersChanged()
    {
        ApplyFilter();
        if (_loadingFilters) return;
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

    private void ApplyFilter()
    {
        Rows.Refresh();
        var visible = Rows.Cast<PalletRow>().ToList();
        KpiCount = visible.Count;
        KpiOpen = visible.Count(r => !r.Status.IsClosed());
        KpiDutyRisk = visible.Count(r => r.IsDutyRisk);
        KpiBalance = visible.Sum(r => r.Balance);
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
        if (OnlyDataFindings && r.OpenFindings == 0) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var t = SearchText.Trim();
            bool Has(string? s) => s?.Contains(t, StringComparison.OrdinalIgnoreCase) == true;
            if (!(Has(r.FolioCode) || Has(r.Material) || Has(r.HuOrBatch) || Has(r.Invoice) || Has(r.Qmm) || Has(r.Pallet.CustomsExportReference)))
                return false;
        }
        return true;
    }

    private string FilterDescription =>
        $"{SelectedValueStream?.Label} · {SelectedStatus?.Label}" +
        (string.IsNullOrWhiteSpace(SearchText) ? "" : $" · \"{SearchText}\"") +
        (OnlyDutyRisk ? " · duty risk only" : "");

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
        OnlyDataFindings = false;
    }

    /// <summary>UX-B06: exporta la vista filtrada a un Excel de sólo lectura.</summary>
    [RelayCommand]
    private void Export()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export inbox (read-only)",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"Returns_inbox_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var visible = Rows.Cast<PalletRow>().Select(r => r.Pallet).ToList();
            ExcelExporter.ExportPallets(visible, dlg.FileName, Services.Clock.Today, FilterDescription);
            Message = $"{visible.Count} records exported to {System.IO.Path.GetFileName(dlg.FileName)} (protected, read-only sheet).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export:\n{ex.Message}\n\nIf the file is open in Excel, close it.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
