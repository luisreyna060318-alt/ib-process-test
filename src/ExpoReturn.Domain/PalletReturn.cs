namespace ExpoReturn.Domain;

/// <summary>
/// Un pallet retornado de El Paso WHS a JuP1 (un renglón de la Matriz 2.0).
/// Fiel a las columnas del Excel 2.0 (sin campos adicionales). Los nombres de propiedad coinciden con trk.Pallets_Return_Tracking.
/// </summary>
public sealed class PalletReturn : NotifyBase
{
    // ---------- Identidad ----------
    private int _id; public int Id { get => _id; set => Set(ref _id, value); }
    private int _valueStreamId; public int ValueStreamId { get => _valueStreamId; set { if (Set(ref _valueStreamId, value)) Raise(nameof(FolioCode)); } }
    private int _folioNumber; public int FolioNumber { get => _folioNumber; set { if (Set(ref _folioNumber, value)) Raise(nameof(FolioCode)); } }
    public string FolioCode => $"VS{ValueStreamId}-{FolioNumber:00000}";

    // ---------- Etapa 1 · Solicitud (QMM / solicitante) ----------
    private string? _qmmResponsible; public string? QmmResponsible { get => _qmmResponsible; set => Set(ref _qmmResponsible, value); }
    private string? _requestingDepartment; public string? RequestingDepartment { get => _requestingDepartment; set => Set(ref _requestingDepartment, value); }
    private string? _requestorName; public string? RequestorName { get => _requestorName; set => Set(ref _requestorName, value); }
    private int? _reasonId; public int? ReasonId { get => _reasonId; set => Set(ref _reasonId, value); }
    private string? _returnReasonDetail; public string? ReturnReasonDetail { get => _returnReasonDetail; set => Set(ref _returnReasonDetail, value); }
    private string? _returnedMaterial; public string? ReturnedMaterial { get => _returnedMaterial; set => Set(ref _returnedMaterial, value); }
    private decimal? _unitCostUsd; public decimal? UnitCostUsd { get => _unitCostUsd; set { if (Set(ref _unitCostUsd, value)) Raise(nameof(SapUsdTotal)); } }
    private string _unitCostSource = "PENDING"; public string UnitCostSource { get => _unitCostSource; set => Set(ref _unitCostSource, value); }
    private int _qtyImported; public int QtyImported { get => _qtyImported; set { if (Set(ref _qtyImported, value)) Raise(nameof(SapUsdTotal)); } }
    private string? _originalHU; public string? OriginalHU { get => _originalHU; set => Set(ref _originalHU, value); }
    private string? _originalBatch; public string? OriginalBatch { get => _originalBatch; set => Set(ref _originalBatch, value); }
    public decimal SapUsdTotal => Math.Round(QtyImported * (UnitCostUsd ?? 0m), 2, MidpointRounding.AwayFromZero);

    // ---------- Etapa 2 · Aduana y logística de entrada (GS/OSD43, QMM, LOP) ----------
    private DateTime? _dateReturnedToWhs; public DateTime? DateReturnedToWhs { get => _dateReturnedToWhs; set => Set(ref _dateReturnedToWhs, value); }
    private DateTime? _importDate; public DateTime? ImportDate { get => _importDate; set => Set(ref _importDate, value); }
    private string? _importInvoice; public string? ImportInvoice { get => _importInvoice; set => Set(ref _importInvoice, value); }
    private DateTime? _importDateElpJup1; public DateTime? ImportDateElpJup1 { get => _importDateElpJup1; set => Set(ref _importDateElpJup1, value); }
    private string? _returnPONumber; public string? ReturnPONumber { get => _returnPONumber; set => Set(ref _returnPONumber, value); }
    private string? _deliveryNumber; public string? DeliveryNumber { get => _deliveryNumber; set => Set(ref _deliveryNumber, value); }

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
    private string? _lowExport; public string? LowExport { get => _lowExport; set => Set(ref _lowExport, value); }
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
    private bool _isLegacy; public bool IsLegacy { get => _isLegacy; set => Set(ref _isLegacy, value); }
    private string? _legacySource; public string? LegacySource { get => _legacySource; set => Set(ref _legacySource, value); }
    public List<string> DataQualityFlags { get; set; } = new();
    public string DataQualityText => string.Join("; ", DataQualityFlags);
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";

    public ClosureDocumentType ClosureType => ClosureDocument.Classify(CustomsExportReference);

    /// <summary>Días desde el cruce; en un registro cerrado se detiene en "Date send Back" (o la confirmación HSE).</summary>
    public int? DaysInJuP1(DateTime today) =>
        ImportDateElpJup1 is null ? null
        : (int)(((Status.IsClosed() ? DateSentBack ?? HseConfirmationDate : null) ?? today).Date - ImportDateElpJup1.Value.Date).TotalDays;

    /// <summary>Copia superficial: el editor trabaja sobre una copia y sólo se guarda al confirmar.</summary>
    public PalletReturn Clone()
    {
        var c = (PalletReturn)MemberwiseClone();
        c.DataQualityFlags = new List<string>(DataQualityFlags);
        c.ClearSubscribers();
        return c;
    }
}
