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
        _edit = pallet?.Clone() ?? services.Pallets.NewDraft(user.ValueStreams.FirstOrDefault(1), user);
        if (pallet is null && _edit.ValueStreamId == 0) _edit.ValueStreamId = 1;
        if (pallet is null) FillCostFromCatalog();
        Hook();
        Perm = new PermissionMap(CanEdit);
        Reevaluate();
        LoadAudit();
    }

    public override string Title => IsNew ? "Nuevo retorno" : $"Retorno {Edit.FolioCode}";

    // ---------- Catálogos para los ComboBox ----------
    public IReadOnlyList<LookupItem> ValueStreamOptions { get; } = Catalogs.ValueStreams.Select(v => new LookupItem(v.Id, v.Code)).ToList();
    public IReadOnlyList<LookupItem> DepartmentOptions { get; } = Catalogs.Departments.Where(d => d.CanRequest).Select(d => new LookupItem(d.Code, $"{d.Code} — {d.Name}")).ToList();
    public IReadOnlyList<Reason> ReasonOptions { get; } = Catalogs.Reasons;
    public IReadOnlyList<LookupItem> IbOrderOptions { get; } = Catalogs.IbOrderTypes.Select(o => new LookupItem(o.Code, $"{o.Code} — {o.Description}")).ToList();
    public IReadOnlyList<LookupItem> CategoryOptions { get; } = Catalogs.StatusCategories.Select(c => new LookupItem(c.Code, $"{c.Description}{(c.Kind == "DISCREPANCY" ? $" · {c.ResponsibleArea ?? "QMM"}" : "")}")).ToList();
    public IReadOnlyList<LookupItem> DutyPaymentOptions { get; } = new[]
    {
        new LookupItem(DutyPaymentStatus.NotRequired, "No requerido"),
        new LookupItem(DutyPaymentStatus.Pending, "Pendiente"),
        new LookupItem(DutyPaymentStatus.Paid, "Pagado"),
    };
    public IReadOnlyList<string> QmmOptions => Services.Champions.QmmFor(Edit.ValueStreamId);

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
    public string FolioDisplay => IsNew ? "Se asignará al guardar" : Edit.FolioCode;
    public string PendingHeader => $"Pendientes para pasar a Solicitado ({PendingIssues.Count})";
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
            return n is not null && n != Edit.ReturnedMaterial ? $"Se guardará como {n}" : null;
        }
    }
    public int? DaysInJuP1 => Edit.DaysInJuP1(Services.Clock.Today);
    public int ErrorCount => Issues.Count(i => i.Severity == Severity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == Severity.Warning) + PendingIssues.Count;
    public int QtyExported => ClosureDocument.Closes(Edit.ClosureType) ? Edit.OldMaterialDeliveredQty ?? 0 : 0;
    public int QtyScrapConfirmed => Edit.HseConfirmationDate is not null ? Edit.ScrapQty ?? 0 : 0;
    public string RoleText => $"Editando como: {User.Role.DisplayName()}";
    public string EditableSummary
    {
        get
        {
            if (IsLegacy) return "Registro histórico importado del Excel: sólo lectura. Revise las observaciones de calidad de datos.";
            if (Edit.Status.IsClosed() && !User.IsAdmin) return "Registro cerrado: sólo Admin QMM/PE puede reabrirlo (con motivo).";
            if (!User.IsAdmin && FieldPermissions.EditableFields(User.Role).Count == 0) return "Su rol no edita campos (sólo lectura).";
            if (!User.CoversValueStream(Edit.ValueStreamId)) return $"Su usuario no está asignado a VS{Edit.ValueStreamId} en la matriz Champions VS: sólo lectura.";
            var n = Fields.All.Count(CanEdit);
            return n == 0 ? "Su rol no edita campos de este registro (sólo lectura)." : $"Puede editar {n} campos (resaltados en verde). El resto pertenece a otras áreas.";
        }
    }

    private bool CanEdit(string field)
    {
        if (field == nameof(PalletReturn.ValueStreamId)) return IsNew;
        if (IsNew)
        {
            // En el alta sólo se captura la Etapa 1 (QMM / solicitante / admin).
            if (!Fields.Stage1.Contains(field)) return false;
            return User.IsAdmin || FieldPermissions.EditableFields(User.Role).Contains(field);
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
        if (e.PropertyName == nameof(PalletReturn.ValueStreamId)) OnPropertyChanged(nameof(QmmOptions));
        Reevaluate();
    }

    /// <summary>Simula la búsqueda de costo MM60 (en producción: cat.Material_Cost_MM60).</summary>
    private void FillCostFromCatalog()
    {
        var pn = Identifiers.NormalizeMaterial(Edit.ReturnedMaterial);
        var known = Services.Repository.GetAll().FirstOrDefault(p => p.ReturnedMaterial == pn && p.UnitCostUsd > 0);
        if (known is null) { if (IsNew) { Edit.UnitCostUsd = null; Edit.UnitCostSource = "PENDING"; } return; }
        Edit.UnitCostUsd = known.UnitCostUsd;
        Edit.UnitCostSource = "MM60";
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
        if (User.IsAdmin && _original is { } o && o.Status.IsClosed() && string.IsNullOrWhiteSpace(SaveReason))
        {
            ShowMessage("Para modificar un registro cerrado capture el motivo (Admin).", true);
            return;
        }
        var result = Services.Pallets.Save(Edit, User, SaveReason);
        if (!result.Success)
        {
            ShowMessage(result.DeniedFields.Count > 0
                ? $"Sin permiso para modificar: {string.Join(", ", result.DeniedFields.Select(FieldLabels.Get))}"
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
        ShowMessage($"Guardado. Folio {Edit.FolioCode} · Estatus: {Edit.StatusName}.", false);
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
        ShowMessage("Cambios descartados.", false);
    }

    [RelayCommand]
    private void Back()
    {
        if (IsDirty && MessageBox.Show("Hay cambios sin guardar. ¿Descartarlos?", "Cambios sin guardar",
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
