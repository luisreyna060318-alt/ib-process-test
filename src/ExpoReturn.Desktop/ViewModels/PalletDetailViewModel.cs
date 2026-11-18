using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExpoReturn.Application;
using ExpoReturn.Desktop.Views;
using ExpoReturn.Domain;
using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Desktop.ViewModels;

/// <summary>
/// Detail / capture of an IB PROCESS: header (request, customs entry, delivery) and its pallets. It works on a COPY of the
/// process (<see cref="Process"/>); <see cref="Edit"/> is the selected pallet. Indicators, status and validations are
/// recalculated while the user types. Fields are enabled by function, department, area and VS (FieldPermissions).
/// </summary>
public sealed partial class PalletDetailViewModel : PageViewModel
{
    private readonly Action _back;
    private IbProcess? _original;

    public PalletDetailViewModel(AppServices services, UserContext user, IbProcess? process, Action back, int? palletId = null) : base(services, user)
    {
        _back = back;
        _original = process;
        var vs = user.EditGrants.Where(g => g.Area == RoleCode.Qmm).Select(g => g.ValueStreamId)
            .Concat(user.EditGrants.Select(g => g.ValueStreamId)).DefaultIfEmpty(1).First();
        _process = process?.Clone() ?? services.Processes.NewDraft(vs, user);
        if (process is null && _process.ValueStreamId == 0) _process.ValueStreamId = 1;
        _edit = _process.Pallets.FirstOrDefault(p => palletId is not null && p.Id == palletId) ?? _process.Pallets.FirstOrDefault() ?? _process.NewPallet(user.NtUser);
        if (process is null) FillCostFromCatalog();
        Hook();
        Perm = new PermissionMap(CanEdit);
        Reevaluate();
        LoadAudit();
        LoadComments();
    }

    public override string Title => IsNew ? "New IB PROCESS" : $"IB PROCESS {Process.FolioCode}";

    // ---------- Combo box catalogs ----------
    /// <summary>Active Value Streams (from the database); a process keeps its own VS even if it was deactivated.</summary>
    public IReadOnlyList<LookupItem> ValueStreamOptions => Catalogs.AllValueStreams
        .Where(v => v.IsActive || v.Id == Process.ValueStreamId).Select(v => new LookupItem(v.Id, v.Code)).ToList();
    /// <summary>Requesting departments; a historical process keeps its old code in the list.</summary>
    public IReadOnlyList<LookupItem> DepartmentOptions => Catalogs.Departments.Where(d => d.CanRequest)
        .Select(d => d.Code)
        .Concat(string.IsNullOrWhiteSpace(Process.RequestingDepartment) ? Array.Empty<string>() : new[] { Process.RequestingDepartment })
        .Distinct(StringComparer.OrdinalIgnoreCase).Select(c => new LookupItem(c, c)).ToList();
    /// <summary>Active reasons (from the database); a process keeps its own reason even if it was deactivated.</summary>
    public IReadOnlyList<Reason> ReasonOptions => Catalogs.AllReasons.Where(r => r.IsActive || r.Id == Process.ReasonId).ToList();
    /// <summary>Status category options of the process VS sheet (exact Excel text).</summary>
    public IReadOnlyList<string> CommentsOptions => Catalogs.CommentsOptions(Process.ValueStreamId, Edit.StatusCategoryCode);

    /// <summary>QMM Responsible: registered users of a QMM-area department with access to the process VS.</summary>
    public IReadOnlyList<LookupItem> QmmUserOptions => UsersOfVs(qmmOnly: true, Process.QmmResponsibleNtUser);
    /// <summary>Requestor: any registered user with access to the process VS.</summary>
    public IReadOnlyList<LookupItem> RequestorUserOptions => UsersOfVs(qmmOnly: false, Process.RequestorNtUser);

    private IReadOnlyList<LookupItem> UsersOfVs(bool qmmOnly, string? current)
    {
        var users = Services.Users.Users().ToDictionary(u => u.NtUser, StringComparer.OrdinalIgnoreCase);
        var nts = Services.Users.Access()
            .Where(a => a.Function != UserFunction.Owner && a.ValueStreamId == Process.ValueStreamId)
            .Where(a => !qmmOnly || RoleInfo.AreaOfDepartment(a.DepartmentCode) == RoleCode.Qmm)
            .Select(a => a.NtUser)
            .Concat(string.IsNullOrWhiteSpace(current) ? Array.Empty<string>() : new[] { current })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return nts.Select(nt => users.TryGetValue(nt, out var u)
                ? new LookupItem(u.NtUser, $"{u.DisplayName} · {u.DepartmentCode ?? "—"}")
                : new LookupItem(nt, nt))
            .OrderBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------- State ----------
    [ObservableProperty] private IbProcess _process;
    [ObservableProperty] private PalletReturn _edit;
    [ObservableProperty] private PermissionMap _perm;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string? _saveReason;
    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private ReconciliationResult _preview = ReconciliationResult.Zero;
    [ObservableProperty] private WorkflowStatus _previewStatus;
    [ObservableProperty] private WorkflowStatus _processStatus;
    [ObservableProperty] private string? _dutyRiskReason;
    [ObservableProperty] private DateTime? _legalDueDate;
    [ObservableProperty] private int? _daysToLegalDue;
    [ObservableProperty] private decimal _processBalance;
    [ObservableProperty] private decimal _processDuties;
    [ObservableProperty] private DateTime? _processDueDate;

    /// <summary>Errors and warnings that are not "required data pending".</summary>
    public ObservableCollection<ValidationIssue> Issues { get; } = new();
    /// <summary>Required data to move to Requested (grouped and collapsible).</summary>
    public ObservableCollection<ValidationIssue> PendingIssues { get; } = new();
    public HelpMap Help => HelpMap.Instance;
    /// <summary>On create the folio does not exist yet.</summary>
    public string FolioDisplay => IsNew ? "Assigned on save" : Process.FolioCode;
    public string PalletDisplay => Edit.Id == 0 && IsNew ? $"Pallet {Edit.LineNo}" : Edit.FolioCode;
    public string PendingHeader => $"Pending to move to Requested ({PendingIssues.Count})";
    public bool HasPending => PendingIssues.Count > 0;
    [ObservableProperty] private bool _pendingExpanded;
    public ObservableCollection<AuditEntry> AuditEntries { get; } = new();

    // ---------- Comments (tab 9) ----------
    public ObservableCollection<ProcessComment> ProcessComments { get; } = new();
    [ObservableProperty] private string _newComment = "";
    public string CommentsHeader => ProcessComments.Count == 0 ? "9 · Comments" : $"9 · Comments ({ProcessComments.Count})";
    public bool CanComment => !IsNew && FieldPermissions.CanComment(User, Context);
    public string CommentHint => IsNew ? "Save the IB PROCESS first; then comments can be added."
        : CanComment ? $"{NewComment.Trim().Length} / {ProcessService.CommentMaxLength} characters"
        : "Only the areas of the form (QMM, OSD, LOP, MFO, LOW, HSE) of this VS write comments; you can read them.";
    partial void OnNewCommentChanged(string value) => OnPropertyChanged(nameof(CommentHint));

    private void LoadComments()
    {
        ProcessComments.Clear();
        if (_original is not null)
            try { foreach (var c in Services.Processes.Comments(_original)) ProcessComments.Add(c); }
            catch (Exception ex) { ShowMessage($"Could not read the comments: {ex.Message}", true); }
        foreach (var n in new[] { nameof(CommentsHeader), nameof(CanComment), nameof(CommentHint) }) OnPropertyChanged(n);
    }

    [RelayCommand]
    private void AddComment()
    {
        if (_original is null) { ShowMessage("Save the IB PROCESS first, then add comments.", true); return; }
        var (ok, message) = Services.Processes.AddComment(_original, User, NewComment);
        ShowMessage(message, !ok);
        if (!ok) return;
        NewComment = "";
        LoadComments();
    }

    public bool IsNew => _original is null;
    public bool IsLegacy => Process.IsLegacy;
    public bool IsReadOnlyRecord => !Fields.All.Any(CanEdit);
    public string PreviewStatusName => PreviewStatus.DisplayName();
    public string ProcessStatusName => ProcessStatus.DisplayName();
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

    /// <summary>The saved version of the selected pallet (null while it is new).</summary>
    private PalletReturn? OriginalPallet => Edit.Id == 0 ? null : _original?.Pallets.FirstOrDefault(p => p.Id == Edit.Id);
    private IbProcess Context => _original ?? Process;

    public bool CanAddPallet => FieldPermissions.CanAddPallet(User, Context);
    public bool CanRemovePallet => Process.Pallets.Count > 1 && FieldPermissions.CanRemovePallet(User, Context, OriginalPallet ?? Edit);
    public bool CanDeleteProcess => !IsNew && FieldPermissions.CanDelete(User, _original!);

    public string EditableSummary
    {
        get
        {
            if (IsNew && !FieldPermissions.CanCreate(User, Process.ValueStreamId))
                return $"The request is always made by QMM: only QMM of VS{Process.ValueStreamId} (or the Owner) creates IB processes.";
            if (!User.IsOwner && !User.EditGrants.Any()) return "Your function is Viewer (read-only). The Owner approves your registration in Requests.";
            if (!User.IsOwner && !User.EditGrants.Any(g => FieldPermissions.IsFormArea(g.Area)))
                return "Your department is not an area of the form: you read the IB processes of your VS (read-only).";
            if (!User.CoversValueStream(Process.ValueStreamId)) return $"You do not capture in VS{Process.ValueStreamId}: read-only.";
            var n = Fields.All.Count(CanEdit);
            if (n == 0 && Edit.Status.IsClosed()) return "Closed pallet: only Owner, Champion and Proxy change it.";
            return n == 0 ? "Your area does not edit fields of this process (read-only)." : $"You can edit {n} fields (highlighted in green, in the tab of your area). The rest belong to other areas.";
        }
    }

    private bool CanEdit(string field)
    {
        if (field == nameof(IbProcess.ValueStreamId)) return IsNew;
        if (IsNew && !FieldPermissions.CanCreate(User, Process.ValueStreamId)) return false;
        var header = Fields.Process.Contains(field) || Fields.Linked.ContainsKey(field);
        return FieldPermissions.CanEdit(User, Context, header ? null : OriginalPallet ?? Edit, field);
    }

    protected override void UserChanged() => RefreshPermissions();

    private void RefreshPermissions()
    {
        Perm = new PermissionMap(CanEdit);
        foreach (var n in new[] { nameof(EditableSummary), nameof(IsReadOnlyRecord), nameof(RoleText), nameof(CanAddPallet),
                                  nameof(CanRemovePallet), nameof(CanDeleteProcess) })
            OnPropertyChanged(n);
    }

    // ---------- Change tracking ----------
    private void Hook()
    {
        Process.PropertyChanged += OnProcessChanged;
        Process.Pallets.CollectionChanged += OnPalletsChanged;
        foreach (var p in Process.Pallets) p.PropertyChanged += OnPalletChanged;
    }

    private void Unhook()
    {
        Process.PropertyChanged -= OnProcessChanged;
        Process.Pallets.CollectionChanged -= OnPalletsChanged;
        foreach (var p in Process.Pallets) p.PropertyChanged -= OnPalletChanged;
    }

    private static readonly HashSet<string> Calculated = new()
    {
        nameof(IbProcess.PalletCount), nameof(IbProcess.QtyImported), nameof(IbProcess.SapUsdTotal), nameof(IbProcess.BalanceGeneralUsd),
        nameof(IbProcess.DutiesToPayUsd), nameof(IbProcess.IsDutyRisk), nameof(IbProcess.LegalDueDate), nameof(IbProcess.Status),
        nameof(IbProcess.StatusName), nameof(IbProcess.PartNumbersText), nameof(IbProcess.PoNumbersText), nameof(IbProcess.FolioCode),
    };

    private bool _syncing;

    private void OnProcessChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || e.PropertyName is null || Calculated.Contains(e.PropertyName)) return;
        IsDirty = true;
        _syncing = true;
        try
        {
            switch (e.PropertyName)
            {
                case nameof(IbProcess.QmmResponsibleNtUser):
                    Process.QmmResponsible = NameOf(Process.QmmResponsibleNtUser) ?? Process.QmmResponsible;
                    break;
                case nameof(IbProcess.RequestorNtUser):
                    Process.RequestorName = NameOf(Process.RequestorNtUser) ?? Process.RequestorName;
                    var dept = Services.Users.Find(Process.RequestorNtUser ?? "")?.DepartmentCode;
                    if (string.IsNullOrWhiteSpace(Process.RequestingDepartment) && !string.IsNullOrWhiteSpace(dept)
                        && Catalogs.Departments.Any(d => d.CanRequest && string.Equals(d.Code, dept, StringComparison.OrdinalIgnoreCase)))
                        Process.RequestingDepartment = dept;
                    break;
                case nameof(IbProcess.ValueStreamId):
                    foreach (var n in new[] { nameof(QmmUserOptions), nameof(RequestorUserOptions), nameof(CommentsOptions) }) OnPropertyChanged(n);
                    Perm = new PermissionMap(CanEdit);
                    break;
            }
        }
        finally { _syncing = false; }
        Reevaluate();
    }

    private string? NameOf(string? ntUser) =>
        string.IsNullOrWhiteSpace(ntUser) ? null : Services.Users.Find(ntUser)?.DisplayName ?? ntUser;

    private void OnPalletChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || e.PropertyName is nameof(PalletReturn.Financials) or nameof(PalletReturn.Status) or nameof(PalletReturn.StatusName)
            or nameof(PalletReturn.FolioCode)) return;
        if (Fields.Process.Contains(e.PropertyName ?? "")) return;     // forwarded header: handled by OnProcessChanged
        IsDirty = true;
        if (ReferenceEquals(sender, Edit) && e.PropertyName == nameof(PalletReturn.ReturnedMaterial)) FillCostFromCatalog();
        Reevaluate();
    }

    private void OnPalletsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (PalletReturn p in e.OldItems ?? Array.Empty<PalletReturn>()) p.PropertyChanged -= OnPalletChanged;
        foreach (PalletReturn p in e.NewItems ?? Array.Empty<PalletReturn>()) p.PropertyChanged += OnPalletChanged;
        IsDirty = true;
        Reevaluate();
    }

    /// <summary>Pallet chosen in the list (the list briefly selects nothing while a pallet is removed: ignored).</summary>
    public PalletReturn? SelectedPallet
    {
        get => Edit;
        set { if (value is not null && !ReferenceEquals(value, Edit)) Edit = value; }
    }

    partial void OnEditChanged(PalletReturn value)
    {
        OnPropertyChanged(nameof(SelectedPallet));
        if (value is null) return;
        Perm = new PermissionMap(CanEdit);
        foreach (var n in new[] { nameof(EditableSummary), nameof(CanRemovePallet), nameof(CommentsOptions), nameof(PalletDisplay) }) OnPropertyChanged(n);
        Reevaluate();
    }

    /// <summary>MM60 unit cost of the part number, from the cost master the Owner loads (Import MM60 file).</summary>
    private void FillCostFromCatalog()
    {
        _syncing = true;
        try
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
        finally { _syncing = false; }
    }

    private void Reevaluate()
    {
        var e = Services.Processes.Evaluate(Process, _original);
        ProcessStatus = e.Status;
        ProcessBalance = e.Pallets.Values.Sum(x => x.Financials.BalanceGeneralUsd);
        ProcessDuties = e.Pallets.Values.Sum(x => x.Financials.DutiesToPayUsd);
        ProcessDueDate = e.Pallets.Values.Where(x => !x.Status.IsClosed() && x.Risk.LegalDueDate is not null).Select(x => x.Risk.LegalDueDate).Min();
        if (e.Pallets.TryGetValue(Edit, out var pe))
        {
            Preview = pe.Financials;
            PreviewStatus = pe.Status;
            DutyRiskReason = pe.Risk.Reason;
            LegalDueDate = pe.Risk.LegalDueDate;
            DaysToLegalDue = pe.Risk.DaysToLegalDue;
        }
        FillIssues(e.Issues);
        foreach (var name in new[] { nameof(PreviewStatusName), nameof(ProcessStatusName), nameof(ClosureText), nameof(NormalizedMaterialHint),
                                     nameof(DaysInJuP1), nameof(QtyExported), nameof(QtyScrapConfirmed), nameof(FolioDisplay), nameof(PalletDisplay),
                                     nameof(CanRemovePallet) })
            OnPropertyChanged(name);
    }

    private void FillIssues(IEnumerable<ValidationIssue> issues)
    {
        Issues.Clear();
        PendingIssues.Clear();
        foreach (var i in issues.OrderBy(i => i.Severity).ThenBy(i => i.Pallet ?? 0))
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

    // ---------- Commands ----------
    [RelayCommand]
    private void AddPallet()
    {
        if (!CanAddPallet) { ShowMessage("No permission to add pallets to this IB PROCESS.", true); return; }
        var p = Process.NewPallet(User.NtUser);
        Edit = p;
        FillCostFromCatalog();
        SelectedTab = 1;
        ShowMessage($"Pallet {p.LineNo} added (PO and PO date copied from the previous pallet).", false);
    }

    [RelayCommand]
    private void RemovePallet()
    {
        if (Process.Pallets.Count <= 1) { ShowMessage("An IB PROCESS needs at least one pallet. To remove everything, delete the process.", true); return; }
        if (!CanRemovePallet) { ShowMessage("No permission to remove this pallet.", true); return; }
        var p = Edit;
        var saved = p.Id != 0;
        if (MessageBox.Show(saved
                    ? $"Delete pallet {p.LineNo} ({p.ReturnedMaterial} · {p.OriginalHU ?? p.OriginalBatch})? It is deleted when you save; the audit log keeps a copy."
                    : $"Remove pallet {p.LineNo}?",
                "Remove pallet", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var index = Process.Pallets.IndexOf(p);
        Process.RemovePallet(p);
        Edit = Process.Pallets[Math.Min(index, Process.Pallets.Count - 1)];
        ShowMessage(saved ? "Pallet removed. Save to confirm (enter the reason)." : "Pallet removed.", false);
    }

    [RelayCommand]
    private void Save()
    {
        if (_original is { } o && o.Status.IsClosed() && o.Pallets.Count > 0 && string.IsNullOrWhiteSpace(SaveReason))
        {
            ShowMessage("To change a closed IB PROCESS, enter the reason.", true);
            return;
        }
        if (_original is not null && _original.Pallets.Any(op => Process.Pallets.All(p => p.Id != op.Id)) && string.IsNullOrWhiteSpace(SaveReason))
        {
            ShowMessage("You removed saved pallets: enter the reason before saving.", true);
            return;
        }
        var repeated = Services.Processes.PoInOtherProcesses(Process);
        if (repeated.Count > 0)
        {
            var detail = string.Join("\n", repeated.Select(r => $"PO {r.PoNumber}: {string.Join(", ", r.Folios.Take(8))}{(r.Folios.Count > 8 ? " …" : "")}"));
            if (MessageBox.Show($"Este PO ya se encuentra registrado en otro IB PROCESS desea continuar?\n\n{detail}",
                    "PO already registered", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                ShowMessage("Not saved. Your data is still here.", false);
                return;
            }
        }
        var selectedLine = Edit.LineNo;
        var result = Services.Processes.Save(Process, User, SaveReason);
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
        Reload(result.Saved!, selectedLine);
        SaveReason = null;
        ShowMessage($"Saved. IB PROCESS {Process.FolioCode} · {Process.PalletCount} pallet(s) · Status: {ProcessStatusName}.", false);
    }

    private void Reload(IbProcess saved, int selectedLine)
    {
        Unhook();
        _original = saved;
        Process = saved.Clone();
        Edit = Process.Pallets.FirstOrDefault(p => p.LineNo == selectedLine) ?? Process.Pallets.First();
        Hook();
        IsDirty = false;
        RefreshPermissions();
        Reevaluate();
        LoadAudit();
        LoadComments();
        foreach (var n in new[] { nameof(Title), nameof(IsNew), nameof(FolioDisplay), nameof(IsLegacy), nameof(QmmUserOptions), nameof(RequestorUserOptions) })
            OnPropertyChanged(n);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_original is null) { _back(); return; }
        if (!CanDeleteProcess) { ShowMessage("Only the Owner, or Champion / Proxy of the requesting department, deletes an IB PROCESS.", true); return; }
        var reason = InputDialog.Ask("Delete IB PROCESS",
            $"Delete {_original.FolioCode} with its {_original.PalletCount} pallet(s)? This cannot be undone; the audit log keeps a copy.\n\nReason:",
            System.Windows.Application.Current?.MainWindow);
        if (reason is null) return;
        var result = Services.Processes.Delete(_original, User, reason);
        if (!result.Success) { ShowMessage(result.Summary, true); return; }
        IsDirty = false;
        MessageBox.Show($"{_original.FolioCode} deleted.", "IB PROCESS deleted", MessageBoxButton.OK, MessageBoxImage.Information);
        _back();
    }

    [RelayCommand]
    private void Discard()
    {
        if (_original is null) { _back(); return; }
        Reload(_original, Edit.LineNo);
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
        if (issue.Pallet is { } line && Process.Pallets.FirstOrDefault(p => p.LineNo == line) is { } pallet) Edit = pallet;
        SelectedTab = TabOf(issue.Field);
    }

    /// <summary>Tab of each field (the tab of the area that fills it, as in the Excel 2.0).</summary>
    public static int TabOf(string field) => field switch
    {
        nameof(IbProcess.QmmResponsible) or nameof(IbProcess.QmmResponsibleNtUser) or nameof(IbProcess.RequestingDepartment)
            or nameof(IbProcess.RequestorName) or nameof(IbProcess.RequestorNtUser) or nameof(IbProcess.ReasonId)
            or nameof(IbProcess.ReturnReasonDetail) or nameof(IbProcess.ValueStreamId) => 0,
        nameof(PalletReturn.ReturnedMaterial) or nameof(PalletReturn.UnitCostUsd) or nameof(PalletReturn.QtyImported)
            or nameof(PalletReturn.OriginalHU) or nameof(PalletReturn.OriginalBatch) or nameof(PalletReturn.DateReturnedToWhs)
            or nameof(PalletReturn.ReturnPONumber) => 1,
        nameof(IbProcess.ImportDate) or nameof(IbProcess.ImportInvoice) or nameof(IbProcess.ImportDateElpJup1)
            or nameof(PalletReturn.CustomsExportReference) => 2,
        nameof(IbProcess.DeliveryNumber) => 3,
        nameof(PalletReturn.ScrapGrNumber) => 4,
        nameof(PalletReturn.LowExportNumber) or nameof(PalletReturn.LowExportDate) => 5,
        nameof(PalletReturn.HseScrapKg) or nameof(PalletReturn.HseConfirmationDate) => 6,
        nameof(PalletReturn.StatusCategoryCode) => 8,
        _ when Fields.Stage3.Contains(field) => 4,
        _ => 7,
    };

    /// <summary>Ctrl+1…9 and Ctrl+0 (Audit) change tab.</summary>
    [RelayCommand]
    private void GoToTab(string? index)
    {
        if (int.TryParse(index, out var i) && i is >= 1 and <= 10) SelectedTab = i - 1;
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }
}
