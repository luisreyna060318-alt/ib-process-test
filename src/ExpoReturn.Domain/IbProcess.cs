using System.Collections.ObjectModel;

namespace ExpoReturn.Domain;

/// <summary>
/// IB PROCESS: one return request with one or more pallets (IB_PROCESS_PROCESSES). The request, customs entry and
/// delivery number belong to the process; part number, quantity, HU or batch, PO and the whole execution belong to each
/// pallet. Balance, duty risk and status are calculated per pallet and summed / combined here.
/// </summary>
public sealed class IbProcess : NotifyBase
{
    private int _id; public int Id { get => _id; set => Set(ref _id, value); }
    private int _valueStreamId; public int ValueStreamId { get => _valueStreamId; set { if (Set(ref _valueStreamId, value)) RaiseFolio(); } }
    private int _folioNumber; public int FolioNumber { get => _folioNumber; set { if (Set(ref _folioNumber, value)) RaiseFolio(); } }
    public string FolioCode => $"VS{ValueStreamId}-{FolioNumber:00000}";

    // ---------- Request (QMM / requesting department) ----------
    private string? _qmmResponsible; public string? QmmResponsible { get => _qmmResponsible; set => Set(ref _qmmResponsible, value); }
    /// <summary>Registered user who is QMM Responsible (new processes; historical ones only have the name).</summary>
    private string? _qmmResponsibleNtUser; public string? QmmResponsibleNtUser { get => _qmmResponsibleNtUser; set => Set(ref _qmmResponsibleNtUser, value); }
    private string? _requestingDepartment; public string? RequestingDepartment { get => _requestingDepartment; set => Set(ref _requestingDepartment, value); }
    private string? _requestorName; public string? RequestorName { get => _requestorName; set => Set(ref _requestorName, value); }
    /// <summary>Registered user who requests: a Member works on the processes they created or request.</summary>
    private string? _requestorNtUser; public string? RequestorNtUser { get => _requestorNtUser; set => Set(ref _requestorNtUser, value); }
    private int? _reasonId; public int? ReasonId { get => _reasonId; set => Set(ref _reasonId, value); }
    private string? _returnReasonDetail; public string? ReturnReasonDetail { get => _returnReasonDetail; set => Set(ref _returnReasonDetail, value); }

    // ---------- Customs entry and delivery (OSD, LOP) ----------
    private DateTime? _importDate; public DateTime? ImportDate { get => _importDate; set => Set(ref _importDate, value); }
    private string? _importInvoice; public string? ImportInvoice { get => _importInvoice; set => Set(ref _importInvoice, value); }
    private DateTime? _importDateElpJup1; public DateTime? ImportDateElpJup1 { get => _importDateElpJup1; set => Set(ref _importDateElpJup1, value); }
    private string? _deliveryNumber; public string? DeliveryNumber { get => _deliveryNumber; set => Set(ref _deliveryNumber, value); }

    // ---------- Control ----------
    private bool _isLegacy; public bool IsLegacy { get => _isLegacy; set => Set(ref _isLegacy, value); }
    private string? _legacySource; public string? LegacySource { get => _legacySource; set => Set(ref _legacySource, value); }
    /// <summary>Calculated: validations an Excel 2.0 process still breaks (inbox filter "To homogenize"); 0 for new ones.</summary>
    public int OpenFindings { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    /// <summary>Optimistic concurrency of the header (each pallet has its own).</summary>
    public int RowVersion { get; set; }

    public ObservableCollection<PalletReturn> Pallets { get; private set; } = new();

    /// <summary>Adds a pallet to this process (it takes the next line number).</summary>
    public PalletReturn AddPallet(PalletReturn pallet)
    {
        pallet.AttachTo(this);
        // Keeps its line number (loaded from the database) unless it is missing or already taken in this process.
        if (pallet.LineNo <= 0 || Pallets.Any(p => !ReferenceEquals(p, pallet) && p.LineNo == pallet.LineNo))
            pallet.LineNo = Pallets.Count == 0 ? 1 : Pallets.Max(p => p.LineNo) + 1;
        Pallets.Add(pallet);
        RaiseTotals();
        return pallet;
    }

    /// <summary>New pallet with the PO and PO date of the last one (usually the same for every pallet).</summary>
    public PalletReturn NewPallet(string createdBy)
    {
        var last = Pallets.LastOrDefault();
        return AddPallet(new PalletReturn
        {
            ReturnPONumber = last?.ReturnPONumber, DateReturnedToWhs = last?.DateReturnedToWhs, CreatedBy = createdBy,
        });
    }

    public void RemovePallet(PalletReturn pallet)
    {
        Pallets.Remove(pallet);
        RaiseTotals();
    }

    // ---------- Calculated from the pallets ----------
    public int PalletCount => Pallets.Count;
    public int QtyImported => Pallets.Sum(p => p.QtyImported);
    public decimal SapUsdTotal => Pallets.Sum(p => p.SapUsdTotal);
    public decimal BalanceGeneralUsd => Pallets.Sum(p => p.Financials.BalanceGeneralUsd);
    public decimal DutiesToPayUsd => Pallets.Sum(p => p.Financials.DutiesToPayUsd);
    public bool IsDutyRisk => Pallets.Any(p => p.IsDutyRisk);
    /// <summary>Earliest IMMEX due date of the pallets still open.</summary>
    public DateTime? LegalDueDate => Pallets.Where(p => !p.Status.IsClosed() && p.LegalDueDate is not null).Select(p => p.LegalDueDate).Min();
    public WorkflowStatus Status => StatusOf(Pallets.Select(p => p.Status).ToList());
    public string StatusName => Status.DisplayName();
    public string PartNumbersText => string.Join(", ", Pallets.Select(p => p.ReturnedMaterial).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
    public string PoNumbersText => string.Join(", ", Pallets.Select(p => p.ReturnPONumber).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());

    /// <summary>
    /// Status of a process: Draft without pallets; Duty risk if any pallet is; else the least advanced open pallet;
    /// when every pallet is closed: Closed (duty paid) if any is, else Exported if any is, else Scrap confirmed.
    /// </summary>
    public static WorkflowStatus StatusOf(IReadOnlyList<WorkflowStatus> pallets)
    {
        if (pallets.Count == 0) return WorkflowStatus.Draft;
        if (pallets.Contains(WorkflowStatus.DutyRisk)) return WorkflowStatus.DutyRisk;
        var open = pallets.Where(s => !s.IsClosed()).ToList();
        if (open.Count > 0) return open.Min();
        if (pallets.Contains(WorkflowStatus.ClosedDutyPaid)) return WorkflowStatus.ClosedDutyPaid;
        return pallets.Contains(WorkflowStatus.Exported) ? WorkflowStatus.Exported : WorkflowStatus.Scrapped;
    }

    /// <summary>Tells the screens that the calculated values changed (after a recalculation).</summary>
    public void RaiseTotals()
    {
        foreach (var n in new[] { nameof(PalletCount), nameof(QtyImported), nameof(SapUsdTotal), nameof(BalanceGeneralUsd), nameof(DutiesToPayUsd),
                                  nameof(IsDutyRisk), nameof(LegalDueDate), nameof(Status), nameof(StatusName), nameof(PartNumbersText), nameof(PoNumbersText) })
            Raise(n);
    }

    private void RaiseFolio()
    {
        Raise(nameof(FolioCode));
        foreach (var p in Pallets) p.RaiseFolio();
    }

    /// <summary>Deep copy: the editor works on a copy (header and pallets) and only saves when confirmed.</summary>
    public IbProcess Clone()
    {
        var c = (IbProcess)MemberwiseClone();
        c.ClearSubscribers();
        c.Pallets = new ObservableCollection<PalletReturn>();
        foreach (var p in Pallets) c.Pallets.Add(p.CopyFor(c));
        return c;
    }
}
