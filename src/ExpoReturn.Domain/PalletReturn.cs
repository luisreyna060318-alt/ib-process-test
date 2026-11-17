namespace ExpoReturn.Domain;

/// <summary>
/// One pallet of an IB PROCESS (IB_PROCESS_PALLETS): its part number, quantity, HU or batch, PO and execution.
/// The request, customs entry and delivery number are read from (and written to) its <see cref="Process"/>, so the
/// rules that use them per pallet keep working. A pallet created alone gets its own process (historical rows, tests).
/// Excel 2.0 columns only; the "LOW export" column, which held two values, is split into number and date.
/// Property names map to IB_PROCESS_PALLET_RETURNS columns (see the field dictionary).
/// </summary>
public sealed class PalletReturn : NotifyBase
{
    // ---------- Identity ----------
    private int _id; public int Id { get => _id; set => Set(ref _id, value); }
    private int _lineNo; public int LineNo { get => _lineNo; set { if (Set(ref _lineNo, value)) Raise(nameof(FolioCode)); } }
    public IbProcess Process { get; private set; }

    public PalletReturn()
    {
        Process = new IbProcess();
        Process.Pallets.Add(this);
        _lineNo = 1;
    }

    internal void AttachTo(IbProcess process)
    {
        if (ReferenceEquals(Process, process)) return;
        Process.Pallets.Remove(this);
        Process = process;
    }

    internal void RaiseFolio() => Raise(nameof(FolioCode));

    /// <summary>VS7-00012-01 (pallet 1 of process VS7-00012).</summary>
    public string FolioCode => $"{Process.FolioCode}-{LineNo:00}";

    // ---------- From the process (header) ----------
    public int ValueStreamId { get => Process.ValueStreamId; set { Process.ValueStreamId = value; Raise(); Raise(nameof(FolioCode)); } }
    public int FolioNumber { get => Process.FolioNumber; set { Process.FolioNumber = value; Raise(); Raise(nameof(FolioCode)); } }
    public string? QmmResponsible { get => Process.QmmResponsible; set { Process.QmmResponsible = value; Raise(); } }
    public string? RequestingDepartment { get => Process.RequestingDepartment; set { Process.RequestingDepartment = value; Raise(); } }
    public string? RequestorName { get => Process.RequestorName; set { Process.RequestorName = value; Raise(); } }
    public int? ReasonId { get => Process.ReasonId; set { Process.ReasonId = value; Raise(); } }
    public string? ReturnReasonDetail { get => Process.ReturnReasonDetail; set { Process.ReturnReasonDetail = value; Raise(); } }
    public DateTime? ImportDate { get => Process.ImportDate; set { Process.ImportDate = value; Raise(); } }
    public string? ImportInvoice { get => Process.ImportInvoice; set { Process.ImportInvoice = value; Raise(); } }
    public DateTime? ImportDateElpJup1 { get => Process.ImportDateElpJup1; set { Process.ImportDateElpJup1 = value; Raise(); } }
    public string? DeliveryNumber { get => Process.DeliveryNumber; set { Process.DeliveryNumber = value; Raise(); } }
    public bool IsLegacy { get => Process.IsLegacy; set { Process.IsLegacy = value; Raise(); } }
    public string? LegacySource { get => Process.LegacySource; set { Process.LegacySource = value; Raise(); } }

    // ---------- Original pallet in El Paso (QMM / requestor) ----------
    private string? _returnedMaterial; public string? ReturnedMaterial { get => _returnedMaterial; set => Set(ref _returnedMaterial, value); }
    private decimal? _unitCostUsd; public decimal? UnitCostUsd { get => _unitCostUsd; set { if (Set(ref _unitCostUsd, value)) Raise(nameof(SapUsdTotal)); } }
    private string _unitCostSource = "PENDING"; public string UnitCostSource { get => _unitCostSource; set => Set(ref _unitCostSource, value); }
    private int _qtyImported; public int QtyImported { get => _qtyImported; set { if (Set(ref _qtyImported, value)) Raise(nameof(SapUsdTotal)); } }
    private string? _originalHU; public string? OriginalHU { get => _originalHU; set { if (Set(ref _originalHU, value)) Raise(nameof(PalletIdText)); } }
    private string? _originalBatch; public string? OriginalBatch { get => _originalBatch; set { if (Set(ref _originalBatch, value)) Raise(nameof(PalletIdText)); } }
    /// <summary>What identifies the pallet: the original HU, else the original batch.</summary>
    public string PalletIdText => !string.IsNullOrWhiteSpace(OriginalHU) ? OriginalHU! : !string.IsNullOrWhiteSpace(OriginalBatch) ? OriginalBatch! : "—";
    public decimal SapUsdTotal => Math.Round(QtyImported * (UnitCostUsd ?? 0m), 2, MidpointRounding.AwayFromZero);

    // ---------- Etapa 2 · Aduana y logística de entrada (GS/OSD43, QMM, LOP) ----------
    private DateTime? _dateReturnedToWhs; public DateTime? DateReturnedToWhs { get => _dateReturnedToWhs; set => Set(ref _dateReturnedToWhs, value); }
    private string? _returnPONumber; public string? ReturnPONumber { get => _returnPONumber; set => Set(ref _returnPONumber, value); }

    // ---------- Etapa 3 · Ejecución en planta (MFO / LOW) ----------
    private int? _physicalDeliveredQty; public int? PhysicalDeliveredQty { get => _physicalDeliveredQty; set => Set(ref _physicalDeliveredQty, value); }
    private string? _newMaterial; public string? NewMaterial { get => _newMaterial; set => Set(ref _newMaterial, value); }
    private string? _newHU; public string? NewHU { get => _newHU; set => Set(ref _newHU, value); }
    private string? _reworkBatch; public string? ReworkBatch { get => _reworkBatch; set => Set(ref _reworkBatch, value); }
    private int? _reinsertionQty; public int? ReinsertionQty { get => _reinsertionQty; set => Set(ref _reinsertionQty, value); }
    private DateTime? _dateSentBack; public DateTime? DateSentBack { get => _dateSentBack; set => Set(ref _dateSentBack, value); }
    private int? _oldMaterialDeliveredQty; public int? OldMaterialDeliveredQty { get => _oldMaterialDeliveredQty; set => Set(ref _oldMaterialDeliveredQty, value); }
    private int? _scrapQty; public int? ScrapQty { get => _scrapQty; set => Set(ref _scrapQty, value); }
    private DateTime? _scrapDate; public DateTime? ScrapDate { get => _scrapDate; set => Set(ref _scrapDate, value); }

    // ---------- Etapa 4 · Cierre aduanal de salida / confirmación de scrap ----------
    private int? _lowExportNumber; public int? LowExportNumber { get => _lowExportNumber; set => Set(ref _lowExportNumber, value); }
    private DateTime? _lowExportDate; public DateTime? LowExportDate { get => _lowExportDate; set => Set(ref _lowExportDate, value); }
    private string? _customsExportReference; public string? CustomsExportReference { get => _customsExportReference; set => Set(ref _customsExportReference, value); }
    private string? _scrapGrNumber; public string? ScrapGrNumber { get => _scrapGrNumber; set => Set(ref _scrapGrNumber, value); }
    private decimal? _hseScrapKg; public decimal? HseScrapKg { get => _hseScrapKg; set => Set(ref _hseScrapKg, value); }
    private DateTime? _hseConfirmationDate; public DateTime? HseConfirmationDate { get => _hseConfirmationDate; set => Set(ref _hseConfirmationDate, value); }

    // ---------- Etapa 5 · Clasificación ----------
    private string? _statusCategoryCode; public string? StatusCategoryCode { get => _statusCategoryCode; set => Set(ref _statusCategoryCode, value); }

    // ---------- Resultados del motor (escritos por PalletEvaluator.Apply) ----------
    private ReconciliationResult _financials = ReconciliationResult.Zero; public ReconciliationResult Financials { get => _financials; set => Set(ref _financials, value); }
    private WorkflowStatus _status = WorkflowStatus.Draft; public WorkflowStatus Status { get => _status; set { if (Set(ref _status, value)) Raise(nameof(StatusName)); } }
    public string StatusName => Status.DisplayName();
    private bool _isDutyRisk; public bool IsDutyRisk { get => _isDutyRisk; set => Set(ref _isDutyRisk, value); }
    private string? _dutyRiskReason; public string? DutyRiskReason { get => _dutyRiskReason; set => Set(ref _dutyRiskReason, value); }
    private DateTime? _legalDueDate; public DateTime? LegalDueDate { get => _legalDueDate; set => Set(ref _legalDueDate, value); }

    // ---------- Control / histórico ----------
    public List<string> DataQualityFlags { get; set; } = new();
    public string DataQualityText => string.Join("; ", DataQualityFlags);
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    /// <summary>Optimistic concurrency (IB_PROCESS_PALLET_RETURNS.ROW_VERSION): a save fails if someone saved first.</summary>
    public int RowVersion { get; set; }

    public ClosureDocumentType ClosureType => ClosureDocument.Classify(CustomsExportReference);

    /// <summary>Days since the crossing; for a closed record it stops at the sent-back date (or the HSE confirmation).</summary>
    public int? DaysInJuP1(DateTime today) =>
        ImportDateElpJup1 is null ? null
        : (int)(((Status.IsClosed() ? DateSentBack ?? HseConfirmationDate : null) ?? today).Date - ImportDateElpJup1.Value.Date).TotalDays;

    /// <summary>
    /// Copy for the editor: the whole process is copied (header and every pallet) and the copy of this pallet is
    /// returned, so its header values are the copy's too.
    /// </summary>
    public PalletReturn Clone() => Process.Clone().Pallets[Process.Pallets.IndexOf(this)];

    /// <summary>Copy of this pallet inside another (copied) process.</summary>
    internal PalletReturn CopyFor(IbProcess process)
    {
        var c = (PalletReturn)MemberwiseClone();
        c.DataQualityFlags = new List<string>(DataQualityFlags);
        c.ClearSubscribers();
        c.Process = process;
        return c;
    }
}
