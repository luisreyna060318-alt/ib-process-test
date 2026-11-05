/* =====================================================================
   Script 03 · Vistas de consumo para Power BI (ME/QMM-JuP1)
   Power BI se conecta SOLO al esquema rpt (rol role_exporeturn_bi).
   Las vistas son estables (contrato): si cambia el modelo físico, se
   ajusta la vista, no el reporte.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Parámetro vigente a una fecha dada. */
CREATE OR ALTER FUNCTION cfg.fn_Param (@Key VARCHAR(50), @AsOf DATE)
RETURNS TABLE
AS RETURN
    SELECT TOP (1) p.NumericValue, p.TextValue
    FROM cfg.Parameters p
    WHERE p.ParamKey = @Key
      AND p.ValidFrom <= @AsOf
      AND (p.ValidTo IS NULL OR p.ValidTo >= @AsOf)
    ORDER BY p.ValidFrom DESC;
GO

/* ---------------------------------------------------------------------
   1) Hecho principal: un renglón por pallet, nombres de negocio.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_PalletReturns
AS
SELECT
    t.PalletReturnId,
    t.FolioCode,
    vs.Code                              AS ValueStream,
    t.FolioNumber,
    qu.DisplayName                       AS QmmResponsible,
    d.Code                               AS RequestingDepartment,
    d.FunctionalArea                     AS RequestingArea,
    t.RequestorName,
    r.Description                        AS Reason,
    r.ReasonGroup,
    t.ReturnReasonDetail,
    t.ReturnedMaterial,
    mm.Description                       AS MaterialDescription,
    t.UnitCostUSD,
    t.UnitCostSource,
    t.QtyImported,
    t.SapUsdTotal                        AS SapUsdTotal,
    t.OriginalHU, t.OriginalBatch,
    t.DateReturnedToWhs, t.ImportDate, t.ImportInvoice, t.ImportDateElpJup1,
    t.ReturnPONumber, t.DeliveryNumber, t.DutyPaymentStatus,
    t.IbOrderTypeCode, t.IbOrderNumber,
    t.PhysicalDeliveredQty, t.NewMaterial, t.NewHU, t.ReinsertionQty, t.DateSentBack,
    t.OldMaterialDeliveredQty, t.ScrapQty, t.ScrapDate,
    t.LowExportNumber, t.LowExportDate, t.CustomsExportReference, t.CustomsExportDate,
    t.ScrapGrNumber, t.HseScrapKg, t.HseConfirmationDate,
    t.GreenConeReported, t.RmaNumber,
    -- Cantidades de conciliación
    t.QtyOpenFloor, t.QtyPendingExport, t.QtyPendingHse, t.QtyMissing,
    CASE WHEN t.CustomsExportReference IS NOT NULL THEN ISNULL(t.OldMaterialDeliveredQty,0) ELSE 0 END AS QtyExported,
    CASE WHEN t.HseConfirmationDate   IS NOT NULL THEN ISNULL(t.ScrapQty,0)                ELSE 0 END AS QtyScrapConfirmed,
    -- Indicadores financieros (calculados por el Core)
    t.MfoInventoryUSD, t.HseScrapUSD, t.MfoLowFailureUSD, t.BalanceGeneralUSD, t.DutyExposureUSD,
    dr.NumericValue                      AS DutyRate,
    CAST(t.MfoLowFailureUSD * ISNULL(dr.NumericValue,0) AS DECIMAL(18,2)) AS DutiesToPayUSD,   -- definición BALANCE_ (Excel: MFO-LOW FAILURE * 0.19)
    CASE WHEN t.ImportDateElpJup1 IS NULL THEN 0 ELSE t.SapUsdTotal - t.BalanceGeneralUSD END AS ReconciledUSD,
    -- Estatus
    ws.Code                              AS WorkflowStatus,
    ws.Name                              AS WorkflowStatusName,
    ws.IsClosed,
    sc.Code                              AS StatusCategory,
    sc.Kind                              AS StatusCategoryKind,
    sc.ResponsibleArea                   AS DiscrepancyOwner,
    t.IsDutyRisk, t.DutyRiskReason,
    t.LegalDueDate,
    CASE WHEN t.ImportDateElpJup1 IS NULL THEN NULL
         ELSE DATEDIFF(DAY, t.ImportDateElpJup1, ISNULL(COALESCE(t.CustomsExportDate, t.HseConfirmationDate), CAST(GETDATE() AS DATE))) END AS DaysInJuP1,
    CASE WHEN t.LegalDueDate IS NULL OR ws.IsClosed = 1 THEN NULL
         ELSE DATEDIFF(DAY, CAST(GETDATE() AS DATE), t.LegalDueDate) END AS DaysToLegalDue,
    CASE WHEN t.ImportDateElpJup1 IS NULL OR ws.IsClosed = 1 THEN 'N/A'
         WHEN DATEDIFF(DAY, t.ImportDateElpJup1, GETDATE()) <= 15 THEN '0-15'
         WHEN DATEDIFF(DAY, t.ImportDateElpJup1, GETDATE()) <= 30 THEN '16-30'
         WHEN DATEDIFF(DAY, t.ImportDateElpJup1, GETDATE()) <= 60 THEN '31-60'
         WHEN DATEDIFF(DAY, t.ImportDateElpJup1, GETDATE()) <= 90 THEN '61-90'
         ELSE '>90' END                  AS AgingBucket,
    fp.Code                              AS ImportFiscalPeriod,
    fp.FiscalYear                        AS ImportFiscalYear,
    t.CalcVersion, t.CalculatedAtUtc,
    t.IsLegacy, t.DataQualityFlags,
    t.IsCancelled,
    t.Comments
FROM trk.Pallets_Return_Tracking t
JOIN cat.Catalog_ValueStreams     vs ON vs.ValueStreamId    = t.ValueStreamId
JOIN cat.Catalog_WorkflowStatus   ws ON ws.WorkflowStatusId = t.WorkflowStatusId
LEFT JOIN sec.Users               qu ON qu.UserId           = t.QmmResponsibleUserId
LEFT JOIN cat.Catalog_Departments d  ON d.DepartmentId      = t.RequestingDepartmentId
LEFT JOIN cat.Catalog_Reasons     r  ON r.ReasonId          = t.ReasonId
LEFT JOIN cat.Catalog_StatusCategories sc ON sc.StatusCategoryId = t.StatusCategoryId
LEFT JOIN cat.Material_Cost_MM60  mm ON mm.Material = t.ReturnedMaterial AND mm.ValidTo IS NULL
LEFT JOIN cfg.Fiscal_Periods      fp ON t.ImportDateElpJup1 BETWEEN fp.StartDate AND fp.EndDate
OUTER APPLY cfg.fn_Param('DUTY_RATE', ISNULL(t.ImportDateElpJup1, CAST(t.CreatedAtUtc AS DATE))) dr;
GO

/* ---------------------------------------------------------------------
   2) BALANCE_ · equivalente al bloque 'AFTER JUNE 2025' de cada hoja,
      pero por VS y periodo fiscal (sin fechas hardcodeadas).
      TOTAL IMPORTED, MFO INVENTORY, HSE SCRAP, MFO-LOW FAILURE,
      EXPORTED USD (= Total - abiertos) y DUTIES TO PAY USD.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_Balance_ByVS_Period
AS
SELECT
    v.ValueStream,
    v.ImportFiscalYear,
    v.ImportFiscalPeriod,
    COUNT(*)                                   AS Pallets,
    SUM(v.SapUsdTotal)                         AS TotalImportedUSD,
    SUM(v.MfoInventoryUSD)                     AS MfoInventoryUSD,
    SUM(v.HseScrapUSD)                         AS HseScrapUSD,
    SUM(v.MfoLowFailureUSD)                    AS MfoLowFailureUSD,
    SUM(v.SapUsdTotal) - SUM(v.MfoInventoryUSD) - SUM(v.HseScrapUSD) - SUM(v.MfoLowFailureUSD) AS ExportedUSD,
    SUM(v.BalanceGeneralUSD)                   AS BalanceGeneralUSD,
    SUM(v.DutiesToPayUSD)                      AS DutiesToPayUSD,      -- compatible con BALANCE_ del Excel
    SUM(v.DutyExposureUSD)                     AS DutyExposureUSD,     -- exposición potencial (Balance * tasa)
    SUM(CASE WHEN v.IsDutyRisk = 1 THEN v.DutyExposureUSD ELSE 0 END) AS DutyExposureAtRiskUSD
FROM rpt.vw_PalletReturns v
WHERE v.ImportDateElpJup1 IS NOT NULL
  AND v.IsCancelled = 0
GROUP BY v.ValueStream, v.ImportFiscalYear, v.ImportFiscalPeriod;
GO

/* ---------------------------------------------------------------------
   3) Conciliación de cantidades (bloque 'FINISH GOOD RETURN (QTY)':
      IMPORT / EXPORTED / PHYSICAL INVENTORY / MISSING / NOT REPORTED SCRAP)
      parametrizado por ventana de reporte.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_QtyReconciliation_ByWindow
AS
SELECT
    w.WindowCode,
    w.Description                              AS WindowDescription,
    v.ValueStream,
    SUM(v.QtyImported)                         AS QtyImported,
    SUM(v.QtyExported)                         AS QtyExported,
    SUM(v.QtyScrapConfirmed)                   AS QtyScrapConfirmed,
    SUM(v.QtyOpenFloor)                        AS QtyPhysicalInventory,
    SUM(v.QtyPendingExport)                    AS QtyPendingExport,
    SUM(v.QtyPendingHse)                       AS QtyNotReportedScrap,
    SUM(v.QtyMissing)                          AS QtyMissing,
    SUM(v.QtyImported) - SUM(v.QtyExported) - SUM(v.QtyScrapConfirmed) AS QtyOpenTotal
FROM rpt.vw_PalletReturns v
JOIN cfg.Reporting_Windows w
  ON v.DateReturnedToWhs >= ISNULL(w.StartDate, '19000101')
 AND v.DateReturnedToWhs <= ISNULL(w.EndDate,   '99991231')
WHERE v.ImportDateElpJup1 IS NOT NULL
  AND v.IsCancelled = 0
GROUP BY w.WindowCode, w.Description, v.ValueStream;
GO

/* ---------------------------------------------------------------------
   4) Duty Risk abierto (lista de acción diaria).
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_DutyRisk_Open
AS
SELECT v.FolioCode, v.ValueStream, v.QmmResponsible, v.ReturnedMaterial, v.QtyImported,
       v.WorkflowStatus, v.DutyRiskReason, v.ImportDateElpJup1, v.LegalDueDate,
       v.DaysInJuP1, v.DaysToLegalDue, v.BalanceGeneralUSD, v.DutyExposureUSD,
       v.DiscrepancyOwner
FROM rpt.vw_PalletReturns v
WHERE v.IsClosed = 0 AND v.IsDutyRisk = 1;
GO

/* ---------------------------------------------------------------------
   5) Audit trail legible (quién cambió qué y cuándo).
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_AuditTrail
AS
SELECT a.AuditLogId, a.FolioCode, a.TableName, a.RecordId, a.Action, a.FieldName,
       a.OldValue, a.NewValue, a.NtUser, u.DisplayName, a.ClientApp, a.ChangeReason,
       a.ChangedAtUtc,
       CAST(a.ChangedAtUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Mountain Standard Time' AS DATETIME2(0)) AS ChangedAtLocal
FROM aud.Audit_Logs a
LEFT JOIN sec.Users u ON u.NtUser = a.NtUser;
GO

/* ---------------------------------------------------------------------
   6) Lead time por etapa (desde Status_History).
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW rpt.vw_StageLeadTime
AS
SELECT h.PalletReturnId, t.FolioCode, vs.Code AS ValueStream,
       ws.Code AS Status, h.ChangedAtUtc AS EnteredAtUtc,
       LEAD(h.ChangedAtUtc) OVER (PARTITION BY h.PalletReturnId ORDER BY h.ChangedAtUtc) AS LeftAtUtc,
       DATEDIFF(HOUR, h.ChangedAtUtc,
                ISNULL(LEAD(h.ChangedAtUtc) OVER (PARTITION BY h.PalletReturnId ORDER BY h.ChangedAtUtc), SYSUTCDATETIME())) AS HoursInStatus
FROM trk.Status_History h
JOIN trk.Pallets_Return_Tracking t ON t.PalletReturnId = h.PalletReturnId
JOIN cat.Catalog_ValueStreams vs   ON vs.ValueStreamId = t.ValueStreamId
JOIN cat.Catalog_WorkflowStatus ws ON ws.WorkflowStatusId = h.ToStatusId;
GO
