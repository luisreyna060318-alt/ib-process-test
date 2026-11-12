using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Desktop.ViewModels;

/// <summary>
/// Detalle / captura de un retorno. Trabaja sobre una COPIA (Edit) y recalcula en vivo indicadores,
/// estatus y validaciones mientras el usuario escribe. Los campos se habilitan según rol + VS.
/// </summary>
public sealed partial class PalletDetailViewModel : PageViewModel
{
    private readonly Action _back;
    private PalletReturn? _original;

    public PalletDetailViewModel(AppServices services, UserContext user, PalletReturn? pallet, Action back) : base(services, user)
    {
        _back = back;
        _original = pallet;
        _edit = pallet?.Clone() ?? services.Pallets.NewDraft(user.EditGrants.Select(g => g.ValueStreamId).DefaultIfEmpty(1).First(), user);
        if (pallet is null && _edit.ValueStreamId == 0) _edit.ValueStreamId = 1;
        if (pallet is null) FillCostFromCatalog();
        Hook();
        Perm = new PermissionMap(CanEdit);
        Reevaluate();
        LoadAudit();
    }

    public override string Title => IsNew ? "New return" : $"Return {Edit.FolioCode}";

    // ---------- Catálogos para los ComboBox ----------
    /// <summary>Active Value Streams (from the database); a record keeps its own VS even if it was deactivated.</summary>
    public IReadOnlyList<LookupItem> ValueStreamOptions => Catalogs.AllValueStreams
        .Where(v => v.IsActive || v.Id == Edit.ValueStreamId).Select(v => new LookupItem(v.Id, v.Code)).ToList();
    /// <summary>Requesting departments; a historical record keeps its old (inactive) code in the list.</summary>
    public IReadOnlyList<LookupItem> DepartmentOptions => Catalogs.Departments.Where(d => d.CanRequest)
        .Select(d => d.Code)
        .Concat(string.IsNullOrWhiteSpace(Edit.RequestingDepartment) ? Array.Empty<string>() : new[] { Edit.RequestingDepartment })
        .Distinct(StringComparer.OrdinalIgnoreCase).Select(c => new LookupItem(c, c)).ToList();
    /// <summary>Active reasons (from the database); a record keeps its own reason even if it was deactivated.</summary>
    public IReadOnlyList<Reason> ReasonOptions => Catalogs.AllReasons.Where(r => r.IsActive || r.Id == Edit.ReasonId).ToList();
    /// <summary>Status category options of the record VS sheet (exact Excel text).</summary>
    public IReadOnlyList<string> CommentsOptions => Catalogs.CommentsOptions(Edit.ValueStreamId, Edit.StatusCategoryCode);
    public IReadOnlyList<string> QmmOptions => Services.Users.QmmFor(Edit.ValueStreamId);

    // ---------- Estado ----------
    [ObservableProperty] private PalletReturn _edit;
    [ObservableProperty] private PermissionMap _perm;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string? _saveReason;
    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private ReconciliationResult _preview = ReconciliationResult.Zero;
    [ObservableProperty] private WorkflowStatus _previewStatus;
    [ObservableProperty] private string? _dutyRiskReason;
    [ObservableProperty] private DateTime? _legalDueDate;
    [ObservableProperty] private int? _daysToLegalDue;

    /// <summary>Errores y avisos que no son "dato obligatorio pendiente".</summary>
    public ObservableCollection<ValidationIssue> Issues { get; } = new();
    /// <summary>UX-B01: avisos de datos obligatorios para pasar a Solicitado (se agrupan y se pliegan).</summary>
    public ObservableCollection<ValidationIssue> PendingIssues { get; } = new();
    public HelpMap Help => HelpMap.Instance;
    /// <summary>UX-B02: en el alta el folio aún no existe.</summary>
    public string FolioDisplay => IsNew ? "Assigned on save" : Edit.FolioCode;
    public string PendingHeader => $"Pending to move to Requested ({PendingIssues.Count})";
    public bool HasPending => PendingIssues.Count > 0;
    [ObservableProperty] private bool _pendingExpanded;
    public ObservableCollection<AuditEntry> AuditEntries { get; } = new();

    public bool IsNew => _original is null;
    public bool IsLegacy => Edit.IsLegacy;
    public bool IsReadOnlyRecord => !Fields.All.Any(CanEdit);
    public string PreviewStatusName => PreviewStatus.DisplayName();
    public string ClosureText => ClosureDocument.Describe(Edit.ClosureType);
    public string? NormalizedMaterialHint
    {
        get
        {
            var n = Identifiers.NormalizeMaterial(Edit.ReturnedMaterial);
            return n is not null && n != Edit.ReturnedMaterial ? $"Will be saved as {n}" : null;
        }
    }
    public int? DaysInJuP1 => Edit.DaysInJuP1(Services.Clock.Today);
    public int ErrorCount => Issues.Count(i => i.Severity == Severity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == Severity.Warning) + PendingIssues.Count;
    public int QtyExported => ClosureDocument.Closes(Edit.ClosureType) ? Edit.OldMaterialDeliveredQty ?? 0 : 0;
    public int QtyScrapConfirmed => Edit.HseConfirmationDate is not null ? Edit.ScrapQty ?? 0 : 0;
    public string RoleText => $"Editing as: {User.FunctionText}";
    public string EditableSummary
    {
        get
        {
            if (Edit.Status.IsClosed() && !User.IsOwner) return "Closed record: only the Owner can reopen it (with a reason).";
            if (!User.IsOwner && !User.EditGrants.Any()) return "Your function is Viewer (read-only). The Owner assigns Champion or Proxy in Users & Access.";
            if (!User.CoversValueStream(Edit.ValueStreamId)) return $"You are not Champion or Proxy in VS{Edit.ValueStreamId}: read-only.";
            var n = Fields.All.Count(CanEdit);
            return n == 0 ? "Your area does not edit fields of this record (read-only)." : $"You can edit {n} fields (highlighted in green). The rest belong to other areas.";
        }
    }

    private bool CanEdit(string field)
    {
        if (field == nameof(PalletReturn.ValueStreamId)) return IsNew;
        if (IsNew)
        {
            // On create only stage 1 is captured (QMM / requestor / Owner), in a VS where the user is Champion or Proxy.
            if (!Fields.Stage1.Contains(field)) return false;
            return User.IsOwner || User.EditGrants.Any(g => g.ValueStreamId == Edit.ValueStreamId
                                                           && FieldPermissions.EditableFields(g.Area).Contains(field));
        }
        if (_original is null) return false;
        return FieldPermissions.CanEdit(User, _original, field);
    }

    protected override void UserChanged()
    {
        Perm = new PermissionMap(CanEdit);
        OnPropertyChanged(nameof(EditableSummary));
        OnPropertyChanged(nameof(IsReadOnlyRecord));
        OnPropertyChanged(nameof(RoleText));
    }

    private void Hook() => Edit.PropertyChanged += OnEditChanged;

    private void OnEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PalletReturn.Financials) or nameof(PalletReturn.Status) or nameof(PalletReturn.StatusName)) return;
        IsDirty = true;
        if (e.PropertyName == nameof(PalletReturn.ReturnedMaterial)) FillCostFromCatalog();
        if (e.PropertyName == nameof(PalletReturn.ValueStreamId)) { OnPropertyChanged(nameof(QmmOptions)); OnPropertyChanged(nameof(CommentsOptions)); }
        Reevaluate();
    }

    /// <summary>MM60 unit cost of the part number, from the cost master the Owner loads (Import MM60 file).</summary>
    private void FillCostFromCatalog()
    {
        var cost = Services.MaterialCosts.Find(Edit.ReturnedMaterial);
        if (cost is { UnitCostUsd: > 0 })
        {
            Edit.UnitCostUsd = cost.UnitCostUsd;
            Edit.UnitCostSource = "MM60";
        }
        else
        {
            Edit.UnitCostUsd = null;
            Edit.UnitCostSource = "PENDING";
        }
    }

    private void Reevaluate()
    {
        var e = Services.Pallets.Evaluate(Edit);
        Preview = e.Financials;
        PreviewStatus = e.Status;
        DutyRiskReason = e.Risk.Reason;
        LegalDueDate = e.Risk.LegalDueDate;
        DaysToLegalDue = e.Risk.DaysToLegalDue;
        FillIssues(e.Issues);
        foreach (var name in new[] { nameof(PreviewStatusName), nameof(ClosureText), nameof(NormalizedMaterialHint), nameof(DaysInJuP1),
                                     nameof(QtyExported), nameof(QtyScrapConfirmed), nameof(FolioDisplay) })
            OnPropertyChanged(name);
    }

    private void FillIssues(IEnumerable<ValidationIssue> issues)
    {
        Issues.Clear();
        PendingIssues.Clear();
        foreach (var i in issues.OrderBy(i => i.Severity))
            (i.IsRequiredPending ? PendingIssues : Issues).Add(i);
        foreach (var name in new[] { nameof(ErrorCount), nameof(WarningCount), nameof(PendingHeader), nameof(HasPending) })
            OnPropertyChanged(name);
    }

    private void LoadAudit()
    {
        AuditEntries.Clear();
        if (_original is null) return;
        foreach (var a in Services.Audit.For(_original.Id)) AuditEntries.Add(a);
    }

    // ---------- Comandos ----------
    [RelayCommand]
    private void Save()
    {
        if (User.IsOwner && _original is { } o && o.Status.IsClosed() && string.IsNullOrWhiteSpace(SaveReason))
        {
            ShowMessage("To change a closed record, enter the reason (Owner).", true);
            return;
        }
        var result = Services.Pallets.Save(Edit, User, SaveReason);
        if (!result.Success)
        {
            ShowMessage(result.Cleared.Count > 0
                ? $"A field that already has a value can be changed but not left empty: {string.Join(", ", result.Cleared.Select(FieldLabels.Get))}"
                : result.DeniedFields.Count > 0
                    ? $"No permission to change: {string.Join(", ", result.DeniedFields.Select(FieldLabels.Get))}"
                    : result.Summary, true);
            if (result.Issues.Count > 0) FillIssues(result.Issues);
            PendingExpanded = true;
            return;
        }
        Edit.PropertyChanged -= OnEditChanged;
        _original = result.Saved!;
        Edit = _original.Clone();
        Hook();
        IsDirty = false;
        SaveReason = null;
        Perm = new PermissionMap(CanEdit);
        Reevaluate();
        LoadAudit();
        foreach (var n in new[] { nameof(Title), nameof(IsNew), nameof(EditableSummary), nameof(IsReadOnlyRecord), nameof(FolioDisplay) }) OnPropertyChanged(n);
        ShowMessage($"Saved. Folio {Edit.FolioCode} · Status: {Edit.StatusName}.", false);
    }

    [RelayCommand]
    private void Discard()
    {
        if (_original is null) { _back(); return; }
        Edit.PropertyChanged -= OnEditChanged;
        Edit = _original.Clone();
        Hook();
        IsDirty = false;
        Reevaluate();
        ShowMessage("Changes discarded.", false);
    }

    [RelayCommand]
    private void Back()
    {
        if (IsDirty && MessageBox.Show("There are unsaved changes. Discard them?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        IsDirty = false;
        _back();
    }

    [RelayCommand]
    private void GoToIssue(ValidationIssue? issue)
    {
        if (issue is null) return;
        SelectedTab = Fields.Stage1.Contains(issue.Field) ? 0
            : Fields.Stage2.Contains(issue.Field) ? 1
            : Fields.Stage3.Contains(issue.Field) ? 2
            : Fields.Stage4.Contains(issue.Field) ? 3 : 4;
    }

    /// <summary>UX-B12: Ctrl+1…6 cambia de pestaña.</summary>
    [RelayCommand]
    private void GoToTab(string? index)
    {
        if (int.TryParse(index, out var i) && i is >= 1 and <= 6) SelectedTab = i - 1;
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }
}
