/* =====================================================================
   Script 04 · Staging y transformación del histórico Excel 2.0 -> SQL
   Flujo:
     1. Extractor (ExpoReturn.Migration, C#/ClosedXML) lee las tablas
        estructuradas VS_1, VS_4, VS_5, VS_7, VS_9 y Table3 (MM60) y las
        vuelca TAL CUAL (texto) en stg.Legacy_Matrix_Raw / stg.Legacy_MM60_Raw.
        Guarda también el valor cacheado de las columnas calculadas para
        conciliar contra el Excel.
     2. stg.usp_TransformLegacy limpia, tipa, mapea catálogos y registra
        cada anomalía en stg.Migration_Issues (nunca descarta datos).
     3. El Migration console invoca el motor de cálculo del Core
        (CalcVersion 1 y 2) y escribe stg.Migration_Reconciliation.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE stg.Legacy_Matrix_Raw (
    RawId                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Legacy_Matrix_Raw PRIMARY KEY,
    LoadBatchId           UNIQUEIDENTIFIER NOT NULL,
    SourceFile            NVARCHAR(260) NOT NULL,
    SheetName             VARCHAR(31)   NOT NULL,
    ExcelRow              INT           NOT NULL,
    Folio                 NVARCHAR(50)  NULL,
    QmmResponsible        NVARCHAR(200) NULL,
    RequestingDepartment  NVARCHAR(200) NULL,
    Requestor             NVARCHAR(200) NULL,
    Reason                NVARCHAR(300) NULL,
    ReturnReasonDetail    NVARCHAR(1000) NULL,
    ReturnedMaterial      NVARCHAR(100) NULL,
    UnitCostCached        NVARCHAR(100) NULL,   -- resultado del XLOOKUP (puede ser 'COSTEAR')
    SapUsdTotalCached     NVARCHAR(100) NULL,
    Quantity              NVARCHAR(100) NULL,
    OriginalHU            NVARCHAR(100) NULL,
    OriginalBatch         NVARCHAR(100) NULL,
    DateReturnedToWhs     NVARCHAR(100) NULL,   -- 'Date PO' | 'Date Returned to WHS'
    ImportDate            NVARCHAR(100) NULL,
    Invoice               NVARCHAR(100) NULL,
    ImportDateElpJup1     NVARCHAR(200) NULL,
    ReturnPO              NVARCHAR(100) NULL,
    DeliveryNumber        NVARCHAR(100) NULL,
    NewMaterial           NVARCHAR(100) NULL,
    PhysicalDelivered     NVARCHAR(100) NULL,
    NewHU                 NVARCHAR(100) NULL,
    ReworkBatch           NVARCHAR(100) NULL,
    ReinsertionQty        NVARCHAR(100) NULL,
    DateSentBack          NVARCHAR(100) NULL,
    OldDelivered          NVARCHAR(100) NULL,
    OldDeliveredIsFormula BIT           NOT NULL DEFAULT (0),
    ScrapQty              NVARCHAR(100) NULL,
    ScrapDate             NVARCHAR(100) NULL,
    LowExport             NVARCHAR(400) NULL,
    CustomsExport         NVARCHAR(100) NULL,
    CustomsExportIsFormula BIT          NOT NULL DEFAULT (0),
    GrNumber              NVARCHAR(100) NULL,
    HseKg                 NVARCHAR(100) NULL,
    HseConfirmationDate   NVARCHAR(100) NULL,
    BalanceCached         NVARCHAR(100) NULL,
    MfoInventoryCached    NVARCHAR(100) NULL,
    HseScrapCached        NVARCHAR(100) NULL,
    MfoLowFailureCached   NVARCHAR(100) NULL,
    CommentsStatus        NVARCHAR(400) NULL,   -- 'COMMENTS' | 'Date - Name:' | 'Column2'
    ExtractedAtUtc        DATETIME2(3)  NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Legacy_Matrix_Raw UNIQUE (LoadBatchId, SheetName, ExcelRow)
);
GO

CREATE TABLE stg.Legacy_MM60_Raw (
    LoadBatchId    UNIQUEIDENTIFIER NOT NULL,
    ExcelRow       INT            NOT NULL,
    Material       NVARCHAR(100)  NULL,
    Description    NVARCHAR(200)  NULL,
    Price          NVARCHAR(100)  NULL,
    AbcIndicator   NVARCHAR(10)   NULL,
    Currency       NVARCHAR(10)   NULL,
    BaseUoM        NVARCHAR(10)   NULL,
    PriceUnit      NVARCHAR(100)  NULL,
    CONSTRAINT PK_Legacy_MM60_Raw PRIMARY KEY (LoadBatchId, ExcelRow)
);
GO

CREATE TABLE stg.Migration_Issues (
    IssueId      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Migration_Issues PRIMARY KEY,
    LoadBatchId  UNIQUEIDENTIFIER NOT NULL,
    SheetName    VARCHAR(31)    NOT NULL,
    ExcelRow     INT            NOT NULL,
    IssueCode    VARCHAR(30)    NOT NULL,
    Severity     VARCHAR(8)     NOT NULL,   -- INFO | WARNING | ERROR
    FieldName    VARCHAR(64)    NULL,
    RawValue     NVARCHAR(400)  NULL,
    Detail       NVARCHAR(400)  NULL,
    Resolution   NVARCHAR(400)  NULL,       -- llenado por el Champion en la depuración
    ResolvedBy   VARCHAR(64)    NULL,
    ResolvedAtUtc DATETIME2(3)  NULL
);
CREATE INDEX IX_Migration_Issues ON stg.Migration_Issues (LoadBatchId, IssueCode);
GO

/* Comparación Excel (cacheado) vs CalcVersion 1 (réplica) vs CalcVersion 2. */
CREATE TABLE stg.Migration_Reconciliation (
    LoadBatchId           UNIQUEIDENTIFIER NOT NULL,
    PalletReturnId        INT            NOT NULL,
    SheetName             VARCHAR(31)    NOT NULL,
    ExcelRow              INT            NOT NULL,
    ExcelBalanceUSD       DECIMAL(18,2)  NULL,
    V1BalanceUSD          DECIMAL(18,2)  NOT NULL,
    V2BalanceUSD          DECIMAL(18,2)  NOT NULL,
    V1MatchesExcel        AS (CASE WHEN ABS(ISNULL(ExcelBalanceUSD,0) - V1BalanceUSD) <= 0.01 THEN 1 ELSE 0 END),
    DeltaV2vsExcelUSD     AS (V2BalanceUSD - ISNULL(ExcelBalanceUSD,0)),
    V2StatusCode          VARCHAR(20)    NOT NULL,
    CONSTRAINT PK_Migration_Reconciliation PRIMARY KEY (LoadBatchId, PalletReturnId)
);
GO

/* Mapeo de textos libres heredados -> catálogos (editable por QMM). */
CREATE TABLE stg.Map_StatusCategory (
    LegacyText        NVARCHAR(200) NOT NULL CONSTRAINT PK_Map_StatusCategory PRIMARY KEY,
    StatusCategoryId  SMALLINT      NOT NULL REFERENCES cat.Catalog_StatusCategories(StatusCategoryId)
);
INSERT INTO stg.Map_StatusCategory (LegacyText, StatusCategoryId) VALUES
 (N'EXPORT',1),(N'EXPORTADO',1),(N'SCRAP',2),(N'INVENTORY',3),(N'INVENTARIO',3),(N'ENVIO NACIONAL',4),
 (N'SCRAP SIN SALIDA FISICA',5),(N'NO REPORTADO CORRECTAMENTE',6),(N'NO REPORTADO CON CONO VERDE',7),
 (N'NO LLEGO CON CONO',8),(N'MATERIAL CON CONO NO REPORTADO POR LOW',9),(N'MATERIAL NO LLEGO DE ELP',10),
 (N'LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)',11),(N'MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)',12);
GO

/* ---------------------------------------------------------------------
   Funciones de limpieza
   --------------------------------------------------------------------- */

/* Fecha heredada: ISO del extractor, serial Excel, 'JAN.20.2025',
   'MAR.2.2026', 'Juio/16/2026', '17/07/2026'. 'N/A', '0', '' -> NULL. */
CREATE OR ALTER FUNCTION stg.fn_ParseLegacyDate (@raw NVARCHAR(200))
RETURNS DATE
AS
BEGIN
    DECLARE @s NVARCHAR(200) = UPPER(LTRIM(RTRIM(ISNULL(@raw, N''))));
    IF @s IN (N'', N'N/A', N'NA', N'0', N'-') RETURN NULL;

    -- ISO yyyy-mm-dd (el extractor escribe así las celdas tipo fecha)
    IF @s LIKE N'[12][0-9][0-9][0-9]-[01][0-9]-[0-3][0-9]%'
        RETURN TRY_CONVERT(DATE, LEFT(@s, 10), 23);

    -- Serial Excel
    IF @s NOT LIKE N'%[^0-9.]%' AND TRY_CAST(@s AS FLOAT) BETWEEN 30000 AND 80000
        RETURN DATEADD(DAY, CAST(TRY_CAST(@s AS FLOAT) AS INT) - 2, CAST('1900-01-01' AS DATE));

    -- Tomar sólo el primer token (p.ej. 'JAN.20.2025 MPS26000009')
    IF CHARINDEX(N' ', @s) > 0 SET @s = LEFT(@s, CHARINDEX(N' ', @s) - 1);
    SET @s = REPLACE(REPLACE(@s, N'/', N'.'), N'-', N'.');
    IF LEN(@s) - LEN(REPLACE(@s, N'.', N'')) <> 2 RETURN NULL;

    DECLARE @a NVARCHAR(20) = PARSENAME(@s, 3), @b NVARCHAR(20) = PARSENAME(@s, 2), @c NVARCHAR(20) = PARSENAME(@s, 1);
    DECLARE @y INT = TRY_CAST(@c AS INT), @m INT, @d INT;
    IF @y BETWEEN 0 AND 99 SET @y = 2000 + @y;
    IF @y IS NULL OR @y NOT BETWEEN 2020 AND 2099 RETURN NULL;

    IF @a LIKE N'[A-Z]%'
    BEGIN
        SET @m = CASE LEFT(@a, 3)
                   WHEN N'JAN' THEN 1 WHEN N'ENE' THEN 1 WHEN N'FEB' THEN 2 WHEN N'MAR' THEN 3
                   WHEN N'APR' THEN 4 WHEN N'ABR' THEN 4 WHEN N'MAY' THEN 5 WHEN N'JUN' THEN 6
                   WHEN N'JUL' THEN 7 WHEN N'JUI' THEN 7 /* typo 'Juio' */ WHEN N'AUG' THEN 8 WHEN N'AGO' THEN 8
                   WHEN N'SEP' THEN 9 WHEN N'SET' THEN 9 WHEN N'OCT' THEN 10 WHEN N'NOV' THEN 11
                   WHEN N'DEC' THEN 12 WHEN N'DIC' THEN 12 END;
        SET @d = TRY_CAST(@b AS INT);
    END
    ELSE
    BEGIN
        -- Numérico: se asume dd/mm/yyyy (formato MX de los textos 'del dia 17/07/2026')
        -- salvo que el segundo token sea > 12 (entonces mm/dd/yyyy).
        DECLARE @n1 INT = TRY_CAST(@a AS INT), @n2 INT = TRY_CAST(@b AS INT);
        IF @n2 > 12 SELECT @m = @n1, @d = @n2; ELSE SELECT @d = @n1, @m = @n2;
    END;

    IF @m IS NULL OR @d IS NULL OR @m NOT BETWEEN 1 AND 12 OR @d NOT BETWEEN 1 AND 31 RETURN NULL;
    RETURN TRY_CONVERT(DATE, CONCAT(@y, '-', RIGHT(CONCAT('0', @m), 2), '-', RIGHT(CONCAT('0', @d), 2)), 23);
END;
GO

/* P/N: '0203306644-250' -> '0203.306.644-250'; mayúsculas; sin espacios. */
CREATE OR ALTER FUNCTION stg.fn_NormalizeMaterial (@raw NVARCHAR(100))
RETURNS VARCHAR(18)
AS
BEGIN
    DECLARE @s VARCHAR(100) = UPPER(REPLACE(LTRIM(RTRIM(ISNULL(@raw, N''))), ' ', ''));
    IF @s = '' RETURN NULL;
    IF @s LIKE '[0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z]-%'
        SET @s = CONCAT(LEFT(@s, 4), '.', SUBSTRING(@s, 5, 3), '.', SUBSTRING(@s, 8, 3), SUBSTRING(@s, 11, 10));
    RETURN LEFT(@s, 18);
END;
GO

/* Cantidad entera >= 0; texto/fórmula -> NULL. */
CREATE OR ALTER FUNCTION stg.fn_ParseQty (@raw NVARCHAR(100))
RETURNS INT
AS
BEGIN
    DECLARE @f FLOAT = TRY_CAST(REPLACE(LTRIM(RTRIM(ISNULL(@raw, N''))), N',', N'') AS FLOAT);
    RETURN CASE WHEN @f IS NULL OR @f < 0 OR @f <> FLOOR(@f) THEN NULL ELSE CAST(@f AS INT) END;
END;
GO

/* ---------------------------------------------------------------------
   Transformación set-based: Raw -> trk.Pallets_Return_Tracking (IsLegacy=1)
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE stg.usp_TransformLegacy
    @LoadBatchId UNIQUEIDENTIFIER,
    @RunBy       VARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* 0. Filas vacías del rango de la tabla (sin material ni cantidad) se ignoran. */
    ;WITH r AS (
        SELECT *,
               stg.fn_NormalizeMaterial(ReturnedMaterial) AS MaterialN,
               stg.fn_ParseQty(Quantity)                  AS QtyN
        FROM stg.Legacy_Matrix_Raw
        WHERE LoadBatchId = @LoadBatchId
    )
    SELECT *,
           /* '#' heredado; si viene vacío o duplicado en la hoja -> 900000 + fila (revisión manual) */
           CASE WHEN stg.fn_ParseQty(Folio) IS NULL
                  OR COUNT(*) OVER (PARTITION BY SheetName, stg.fn_ParseQty(Folio)) > 1
                THEN 900000 + ExcelRow ELSE stg.fn_ParseQty(Folio) END AS FolioN
    INTO #r FROM r WHERE MaterialN IS NOT NULL OR QtyN IS NOT NULL;

    /* 1. Bitácora de calidad de datos (DQ). */
    INSERT INTO stg.Migration_Issues (LoadBatchId, SheetName, ExcelRow, IssueCode, Severity, FieldName, RawValue, Detail)
    SELECT @LoadBatchId, SheetName, ExcelRow, x.Code, x.Sev, x.Field, x.Val, x.Detail
    FROM #r
    CROSS APPLY (VALUES
      ('DQ-QTY-INVALID',      'ERROR',   'Quantity',             Quantity,          N'Cantidad importada vacía, no numérica o <= 0'),
      ('DQ-PN-FORMAT',        'ERROR',   'ReturnedMaterial',     ReturnedMaterial,  N'P/N no cumple formato XXXX.XXX.XXX-XX(X)'),
      ('DQ-COST-PENDING',     'WARNING', 'UnitCost',             UnitCostCached,    N'XLOOKUP devolvió COSTEAR / material sin costo MM60'),
      ('DQ-REASON-MISSING',   'WARNING', 'Reason',               Reason,            N'Motivo vacío o fuera del catálogo oficial'),
      ('DQ-DEPT-MISSING',     'WARNING', 'RequestingDepartment', RequestingDepartment, N'Departamento solicitante vacío'),
      ('DQ-HU-FORMAT',        'WARNING', 'OriginalHU',           OriginalHU,        N'HU original no es de 10 dígitos numéricos'),
      ('DQ-NEWHU-FORMAT',     'WARNING', 'NewHU',                NewHU,             N'New HU no es de 10 dígitos numéricos'),
      ('DQ-DATE-TEXT',        'INFO',    'ImportDate',           ImportDate,        N'Fecha capturada como texto (convertida)'),
      ('DQ-DATE-UNPARSEABLE', 'ERROR',   'DateSentBack',         DateSentBack,      N'Fecha no interpretable'),
      ('DQ-ELP-CONCAT',       'INFO',    'ImportDateElpJup1',    ImportDateElpJup1, N'Fecha y factura concatenadas en una celda (separadas)'),
      ('DQ-CUSTOMS-FORMULA',  'ERROR',   'CustomsExport',        CustomsExport,     N'Customs Export contiene fórmula/cantidad en vez de referencia: el Excel lo trató como exportado'),
      ('DQ-DATE-ORDER',       'ERROR',   'ImportDate',           ImportDate,        N'Fecha de importación anterior a Date PO / Returned to WHS'),
      ('DQ-QTY-OVERFLOW',     'ERROR',   'OldDelivered',         OldDelivered,      N'Old Material + Scrap > Quantity importada'),
      ('DQ-QTY-GAP',          'ERROR',   'OldDelivered',         OldDelivered,      N'Old Material + Scrap < Quantity tras cierre de MFO: faltante no reportado'),
      ('DQ-HSE-WITHOUT-QTY',  'ERROR',   'ScrapQty',             ScrapQty,          N'HSE confirmó (Kg/GR/fecha) pero Scrap Qty = 0/vacío'),
      ('DQ-STATUS-TEXT',      'WARNING', 'CommentsStatus',       CommentsStatus,    N'Comentario/estatus fuera de catálogo'),
      ('DQ-FOLIO-DUP',        'WARNING', 'Folio',                Folio,             N'Folio # vacío o duplicado en la hoja: se asignó 900000 + fila')
    ) x(Code, Sev, Field, Val, Detail)
    WHERE (x.Code = 'DQ-QTY-INVALID'      AND (QtyN IS NULL OR QtyN = 0))
       OR (x.Code = 'DQ-PN-FORMAT'        AND (MaterialN IS NULL OR MaterialN NOT LIKE '[0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z]-[0-9A-Z][0-9A-Z]%'))
       OR (x.Code = 'DQ-COST-PENDING'     AND TRY_CAST(UnitCostCached AS DECIMAL(18,6)) IS NULL
                                          AND NOT EXISTS (SELECT 1 FROM cat.Material_Cost_MM60 m WHERE m.Material = MaterialN AND m.ValidTo IS NULL AND m.Price > 0))
       OR (x.Code = 'DQ-REASON-MISSING'   AND NOT EXISTS (SELECT 1 FROM cat.Catalog_Reasons c WHERE c.Description = LTRIM(RTRIM(Reason))))
       OR (x.Code = 'DQ-DEPT-MISSING'     AND NULLIF(LTRIM(RTRIM(RequestingDepartment)), N'') IS NULL AND CHARINDEX(N':', ISNULL(Requestor, N'')) = 0)
       OR (x.Code = 'DQ-HU-FORMAT'        AND OriginalHU IS NOT NULL AND (LEN(LTRIM(RTRIM(OriginalHU))) <> 10 OR LTRIM(RTRIM(OriginalHU)) LIKE N'%[^0-9]%'))
       OR (x.Code = 'DQ-NEWHU-FORMAT'     AND NewHU IS NOT NULL AND (LEN(LTRIM(RTRIM(NewHU))) <> 10 OR LTRIM(RTRIM(NewHU)) LIKE N'%[^0-9]%'))
       OR (x.Code = 'DQ-DATE-TEXT'        AND ImportDate LIKE N'[A-Za-z]%')
       OR (x.Code = 'DQ-DATE-UNPARSEABLE' AND NULLIF(LTRIM(RTRIM(DateSentBack)), N'') IS NOT NULL AND DateSentBack NOT IN (N'N/A', N'0') AND stg.fn_ParseLegacyDate(DateSentBack) IS NULL)
       OR (x.Code = 'DQ-ELP-CONCAT'       AND CHARINDEX(N' ', LTRIM(RTRIM(ImportDateElpJup1))) > 0)
       OR (x.Code = 'DQ-CUSTOMS-FORMULA'  AND (CustomsExportIsFormula = 1 OR CustomsExport NOT LIKE N'%[A-Za-z]%'))
       OR (x.Code = 'DQ-DATE-ORDER'       AND stg.fn_ParseLegacyDate(ImportDate) < stg.fn_ParseLegacyDate(DateReturnedToWhs))
       OR (x.Code = 'DQ-QTY-OVERFLOW'     AND ISNULL(stg.fn_ParseQty(OldDelivered),0) + ISNULL(stg.fn_ParseQty(ScrapQty),0) > QtyN)
       OR (x.Code = 'DQ-QTY-GAP'          AND stg.fn_ParseQty(PhysicalDelivered) IS NOT NULL
                                          AND ISNULL(stg.fn_ParseQty(OldDelivered),0) + ISNULL(stg.fn_ParseQty(ScrapQty),0) < QtyN)
       OR (x.Code = 'DQ-HSE-WITHOUT-QTY'  AND ISNULL(stg.fn_ParseQty(ScrapQty),0) = 0
                                          AND (TRY_CAST(HseKg AS DECIMAL(10,3)) > 0 OR stg.fn_ParseLegacyDate(HseConfirmationDate) IS NOT NULL))
       OR (x.Code = 'DQ-STATUS-TEXT'      AND NULLIF(LTRIM(RTRIM(CommentsStatus)), N'') IS NOT NULL
                                          AND NOT EXISTS (SELECT 1 FROM stg.Map_StatusCategory s WHERE s.LegacyText = UPPER(LTRIM(RTRIM(CommentsStatus)))))
       OR (x.Code = 'DQ-FOLIO-DUP'        AND FolioN >= 900000);

    /* 2. Usuarios QMM históricos como placeholders (se mapean a NT ID después). */
    INSERT INTO sec.Users (NtUser, DisplayName, IsPlaceholder)
    SELECT DISTINCT CONCAT('LEGACY\', UPPER(REPLACE(LTRIM(RTRIM(QmmResponsible)), ' ', '.'))), LTRIM(RTRIM(QmmResponsible)), 1
    FROM #r r
    WHERE NULLIF(LTRIM(RTRIM(QmmResponsible)), N'') IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM sec.Users u WHERE u.DisplayName = LTRIM(RTRIM(r.QmmResponsible)));

    /* 3. Carga a la tabla transaccional (IsLegacy = 1, estatus provisional;
          el Migration console recalcula estatus e indicadores con el Core). */
    BEGIN TRANSACTION;

    INSERT INTO trk.Pallets_Return_Tracking (
        ValueStreamId, FolioNumber, QmmResponsibleUserId, RequestingDepartmentId, RequestorName, ReasonId,
        ReturnReasonDetail, ReturnedMaterial, UnitCostUSD, UnitCostSource, QtyImported, OriginalHU, OriginalBatch,
        DateReturnedToWhs, ImportDate, ImportInvoice, ImportDateElpJup1, ReturnPONumber, ReturnPOItem, DeliveryNumber,
        PhysicalDeliveredQty, NewMaterial, NewHU, ReworkBatch, ReinsertionQty, DateSentBack,
        OldMaterialDeliveredQty, ScrapQty, ScrapDate,
        LowExportNumber, LowExportDate, LowExportNote, CustomsExportReference,
        ScrapGrNumber, HseScrapKg, HseConfirmationDate,
        WorkflowStatusId, StatusCategoryId, CalcVersion, Comments,
        IsLegacy, LegacySourceSheet, LegacySourceRow, DataQualityFlags, CreatedBy)
    SELECT
        vs.ValueStreamId,
        r.FolioN,
        u.UserId,
        COALESCE(dExplicit.DepartmentId, dFromRequestor.DepartmentId),
        LTRIM(RTRIM(CASE WHEN CHARINDEX(N':', r.Requestor) > 0 THEN SUBSTRING(r.Requestor, CHARINDEX(N':', r.Requestor) + 1, 200) ELSE r.Requestor END)),
        rs.ReasonId,
        NULLIF(LTRIM(RTRIM(r.ReturnReasonDetail)), N''),
        ISNULL(r.MaterialN, 'UNKNOWN'),
        COALESCE(TRY_CAST(r.UnitCostCached AS DECIMAL(18,6)), mm.UnitCostUSD),
        CASE WHEN TRY_CAST(r.UnitCostCached AS DECIMAL(18,6)) IS NOT NULL THEN 'LEGACY'
             WHEN mm.UnitCostUSD IS NOT NULL THEN 'MM60' ELSE 'PENDING' END,
        ISNULL(NULLIF(r.QtyN, 0), 1),                       -- 0/NULL -> 1 + issue DQ-QTY-INVALID (revisión obligatoria)
        CASE WHEN NULLIF(LTRIM(RTRIM(r.OriginalHU)), N'') NOT LIKE N'%[^0-9]%' THEN LEFT(LTRIM(RTRIM(r.OriginalHU)), 20) END,
        COALESCE(NULLIF(LTRIM(RTRIM(r.OriginalBatch)), N''),
                 CASE WHEN LTRIM(RTRIM(r.OriginalHU)) LIKE N'[A-Z][A-Z][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]%' THEN LEFT(LTRIM(RTRIM(r.OriginalHU)), 10) END),
        stg.fn_ParseLegacyDate(r.DateReturnedToWhs),
        stg.fn_ParseLegacyDate(r.ImportDate),
        COALESCE(NULLIF(LTRIM(RTRIM(r.Invoice)), N''),
                 CASE WHEN CHARINDEX(N' ', LTRIM(RTRIM(r.ImportDateElpJup1))) > 0 THEN LTRIM(SUBSTRING(LTRIM(RTRIM(r.ImportDateElpJup1)), CHARINDEX(N' ', LTRIM(RTRIM(r.ImportDateElpJup1))) + 1, 20)) END),
        stg.fn_ParseLegacyDate(r.ImportDateElpJup1),
        LEFT(LTRIM(RTRIM(CASE WHEN CHARINDEX(N'-', r.ReturnPO) > 0 THEN LEFT(r.ReturnPO, CHARINDEX(N'-', r.ReturnPO) - 1) ELSE r.ReturnPO END)), 10),
        CASE WHEN CHARINDEX(N'-', r.ReturnPO) > 0 THEN LEFT(LTRIM(RTRIM(SUBSTRING(r.ReturnPO, CHARINDEX(N'-', r.ReturnPO) + 1, 10))), 6) END,
        LEFT(NULLIF(LTRIM(RTRIM(r.DeliveryNumber)), N''), 10),
        stg.fn_ParseQty(r.PhysicalDelivered),
        stg.fn_NormalizeMaterial(r.NewMaterial),
        LEFT(NULLIF(LTRIM(RTRIM(r.NewHU)), N''), 20),
        LEFT(NULLIF(LTRIM(RTRIM(r.ReworkBatch)), N''), 10),
        stg.fn_ParseQty(r.ReinsertionQty),
        stg.fn_ParseLegacyDate(r.DateSentBack),
        stg.fn_ParseQty(r.OldDelivered),
        stg.fn_ParseQty(r.ScrapQty),
        stg.fn_ParseLegacyDate(r.ScrapDate),
        TRY_CAST(SUBSTRING(r.LowExport, NULLIF(CHARINDEX(N'#', r.LowExport), 0) + 1, 2) AS SMALLINT),
        stg.fn_ParseLegacyDate(SUBSTRING(r.LowExport, NULLIF(PATINDEX(N'%[0-3][0-9]/[01][0-9]/20[0-9][0-9]%', r.LowExport), 0), 10)),
        LEFT(NULLIF(LTRIM(RTRIM(r.LowExport)), N''), 200),
        CASE WHEN r.CustomsExportIsFormula = 0 AND r.CustomsExport LIKE N'%[A-Za-z]%' THEN LEFT(LTRIM(RTRIM(r.CustomsExport)), 30) END,
        CASE WHEN LTRIM(RTRIM(r.GrNumber)) NOT LIKE N'%[^0-9]%' AND LEN(LTRIM(RTRIM(r.GrNumber))) > 0 THEN LEFT(LTRIM(RTRIM(r.GrNumber)), 10) END,
        TRY_CAST(r.HseKg AS DECIMAL(10,3)),
        stg.fn_ParseLegacyDate(r.HseConfirmationDate),
        1,                                                  -- provisional: DRAFT
        ms.StatusCategoryId,
        0,                                                  -- 0 = pendiente de recálculo por el Core
        NULLIF(LTRIM(RTRIM(r.CommentsStatus)), N''),
        1, r.SheetName, r.ExcelRow,
        LEFT((SELECT STRING_AGG(i.IssueCode, ';') FROM stg.Migration_Issues i
               WHERE i.LoadBatchId = @LoadBatchId AND i.SheetName = r.SheetName AND i.ExcelRow = r.ExcelRow), 400),
        @RunBy
    FROM #r r
    JOIN cat.Catalog_ValueStreams vs ON vs.LegacySheetName = r.SheetName
    LEFT JOIN sec.Users u ON u.DisplayName = LTRIM(RTRIM(r.QmmResponsible))
    LEFT JOIN cat.Catalog_Departments dExplicit      ON dExplicit.Code = UPPER(LTRIM(RTRIM(r.RequestingDepartment)))
    LEFT JOIN cat.Catalog_Departments dFromRequestor ON CHARINDEX(N':', r.Requestor) > 0
                                                    AND dFromRequestor.Code = UPPER(LTRIM(RTRIM(LEFT(r.Requestor, CHARINDEX(N':', r.Requestor) - 1))))
    LEFT JOIN cat.Catalog_Reasons rs ON rs.Description = LTRIM(RTRIM(r.Reason))
    LEFT JOIN cat.Material_Cost_MM60 mm ON mm.Material = r.MaterialN AND mm.ValidTo IS NULL
    LEFT JOIN stg.Map_StatusCategory ms ON ms.LegacyText = UPPER(LTRIM(RTRIM(r.CommentsStatus)));

    /* Ajuste de contadores de folio para que los nuevos registros continúen la numeración. */
    UPDATE fc SET LastFolio = x.MaxFolio
    FROM trk.Folio_Counters fc
    JOIN (SELECT ValueStreamId, MAX(FolioNumber) AS MaxFolio
          FROM trk.Pallets_Return_Tracking WHERE FolioNumber < 900000 GROUP BY ValueStreamId) x
      ON x.ValueStreamId = fc.ValueStreamId
    WHERE x.MaxFolio > fc.LastFolio;

    COMMIT TRANSACTION;

    SELECT IssueCode, Severity, COUNT(*) AS Occurrences
    FROM stg.Migration_Issues WHERE LoadBatchId = @LoadBatchId
    GROUP BY IssueCode, Severity ORDER BY Severity, Occurrences DESC;
END;
GO

/* Carga del maestro MM60 desde la hoja oculta (Price / Price unit). */
CREATE OR ALTER PROCEDURE stg.usp_LoadMM60
    @LoadBatchId UNIQUEIDENTIFIER,
    @ValidFrom   DATE
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    ;WITH src AS (
        SELECT stg.fn_NormalizeMaterial(Material) AS Material,
               LEFT(Description, 80) AS Description,
               TRY_CAST(Price AS DECIMAL(18,4)) AS Price,
               ISNULL(TRY_CAST(PriceUnit AS INT), 1) AS PriceUnit,
               LEFT(ISNULL(NULLIF(Currency, N''), N'USD'), 3) AS Currency,
               LEFT(BaseUoM, 3) AS BaseUoM, LEFT(NULLIF(AbcIndicator, N''), 1) AS Abc,
               ROW_NUMBER() OVER (PARTITION BY stg.fn_NormalizeMaterial(Material) ORDER BY ExcelRow DESC) AS rn
        FROM stg.Legacy_MM60_Raw
        WHERE LoadBatchId = @LoadBatchId AND NULLIF(LTRIM(RTRIM(Material)), N'') IS NOT NULL
    )
    SELECT * INTO #mm FROM src WHERE rn = 1 AND Price IS NOT NULL AND PriceUnit > 0;

    -- Cierra la vigencia del costo anterior si cambió
    UPDATE c SET ValidTo = DATEADD(DAY, -1, @ValidFrom)
    FROM cat.Material_Cost_MM60 c
    JOIN #mm s ON s.Material = c.Material
    WHERE c.ValidTo IS NULL AND c.ValidFrom < @ValidFrom
      AND (c.Price <> s.Price OR c.PriceUnit <> s.PriceUnit);

    INSERT INTO cat.Material_Cost_MM60 (Material, Description, Price, PriceUnit, Currency, BaseUoM, AbcIndicator, ValidFrom, Source, LoadBatchId)
    SELECT s.Material, s.Description, s.Price, s.PriceUnit, s.Currency, s.BaseUoM, s.Abc, @ValidFrom, 'EXCEL', @LoadBatchId
    FROM #mm s
    WHERE NOT EXISTS (SELECT 1 FROM cat.Material_Cost_MM60 c WHERE c.Material = s.Material AND c.ValidTo IS NULL);

    COMMIT TRANSACTION;
END;
GO
