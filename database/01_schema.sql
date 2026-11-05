/* =====================================================================
   JuP1 (8640) · QMM Process Excellence · Expo & Return FG Follow Up
   Script 01 · Esquemas, catálogos, seguridad, transacción y auditoría
   Motor objetivo: Microsoft SQL Server 2019+ (compatibility level 150)
   Base de datos sugerida: JUP1_QMM_EXPORETURN
   ---------------------------------------------------------------------
   Convenciones:
     cat.*  catálogos maestros            sec.* seguridad / RBAC
     trk.*  datos transaccionales         aud.* bitácora (append-only)
     cfg.*  parámetros versionados        rpt.* vistas para Power BI
     stg.*  staging de migración (ver 04_staging_migration.sql)
   Todas las fechas de auditoría en UTC (DATETIME2). Fechas de negocio
   (aduana, scrap, exportación) en DATE local Ciudad Juárez.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID('cat') IS NULL EXEC('CREATE SCHEMA cat AUTHORIZATION dbo;');
IF SCHEMA_ID('sec') IS NULL EXEC('CREATE SCHEMA sec AUTHORIZATION dbo;');
IF SCHEMA_ID('trk') IS NULL EXEC('CREATE SCHEMA trk AUTHORIZATION dbo;');
IF SCHEMA_ID('aud') IS NULL EXEC('CREATE SCHEMA aud AUTHORIZATION dbo;');
IF SCHEMA_ID('cfg') IS NULL EXEC('CREATE SCHEMA cfg AUTHORIZATION dbo;');
IF SCHEMA_ID('rpt') IS NULL EXEC('CREATE SCHEMA rpt AUTHORIZATION dbo;');
IF SCHEMA_ID('stg') IS NULL EXEC('CREATE SCHEMA stg AUTHORIZATION dbo;');
GO

/* =====================================================================
   1. CATÁLOGOS
   ===================================================================== */

/* Value Streams. El Id ES el número de VS (1,4,5,7,9) para que el folio
   'VS7-00012' sea determinístico y legible sin JOIN. */
CREATE TABLE cat.Catalog_ValueStreams (
    ValueStreamId     TINYINT       NOT NULL CONSTRAINT PK_Catalog_ValueStreams PRIMARY KEY,
    Code              VARCHAR(5)    NOT NULL CONSTRAINT UQ_Catalog_ValueStreams_Code UNIQUE,   -- 'VS1'
    Name              NVARCHAR(100) NOT NULL,
    LegacySheetName   VARCHAR(31)   NULL,      -- hoja del Excel 2.0 ('VS1')
    LegacyTableName   VARCHAR(31)   NULL,      -- tabla estructurada ('VS_1')
    IsActive          BIT           NOT NULL CONSTRAINT DF_Catalog_ValueStreams_IsActive DEFAULT (1),
    SortOrder         TINYINT       NOT NULL CONSTRAINT DF_Catalog_ValueStreams_Sort DEFAULT (0),
    CONSTRAINT CK_Catalog_ValueStreams_Code CHECK (Code = CONCAT('VS', ValueStreamId))
);
GO

/* Departamentos (jerárquico): MFQ -> MFQ1..MFQ9, LOP -> LOP1/LOP2, etc. */
CREATE TABLE cat.Catalog_Departments (
    DepartmentId        SMALLINT      IDENTITY(1,1) NOT NULL CONSTRAINT PK_Catalog_Departments PRIMARY KEY,
    Code                VARCHAR(20)   NOT NULL CONSTRAINT UQ_Catalog_Departments_Code UNIQUE,  -- 'MFQ5', 'GS/OSD43'
    Name                NVARCHAR(100) NOT NULL,
    FunctionalArea      VARCHAR(10)   NOT NULL,   -- área dueña de un bloque del flujo
    ParentDepartmentId  SMALLINT      NULL CONSTRAINT FK_Catalog_Departments_Parent REFERENCES cat.Catalog_Departments(DepartmentId),
    CanRequest          BIT           NOT NULL CONSTRAINT DF_Catalog_Departments_CanRequest DEFAULT (0),
    IsActive            BIT           NOT NULL CONSTRAINT DF_Catalog_Departments_IsActive DEFAULT (1),
    CONSTRAINT CK_Catalog_Departments_Area CHECK (FunctionalArea IN ('QMM','MFO','MFQ','LOP','LOW','GS','HSE','PUQ','OTHER'))
);
GO

/* Motivos oficiales de retorno (selector estricto). */
CREATE TABLE cat.Catalog_Reasons (
    ReasonId            SMALLINT      NOT NULL CONSTRAINT PK_Catalog_Reasons PRIMARY KEY,
    Code                VARCHAR(10)   NOT NULL CONSTRAINT UQ_Catalog_Reasons_Code UNIQUE,     -- 'R01'
    Description         NVARCHAR(150) NOT NULL CONSTRAINT UQ_Catalog_Reasons_Desc UNIQUE,
    ReasonGroup         VARCHAR(20)   NOT NULL,
    RequiresNewMaterial BIT           NOT NULL CONSTRAINT DF_Catalog_Reasons_ReqNewPN DEFAULT (0), -- reflash
    IsActive            BIT           NOT NULL CONSTRAINT DF_Catalog_Reasons_IsActive DEFAULT (1),
    SortOrder           TINYINT       NOT NULL,
    CONSTRAINT CK_Catalog_Reasons_Group CHECK (ReasonGroup IN ('PACKAGING','REFLASH','INSPECTION','COMPLETION'))
);
GO

/* Estatus del flujo (derivado por el motor de reglas del Core). */
CREATE TABLE cat.Catalog_WorkflowStatus (
    WorkflowStatusId  TINYINT      NOT NULL CONSTRAINT PK_Catalog_WorkflowStatus PRIMARY KEY,
    Code              VARCHAR(20)  NOT NULL CONSTRAINT UQ_Catalog_WorkflowStatus_Code UNIQUE,
    Name              NVARCHAR(60) NOT NULL,
    StageNo           TINYINT      NOT NULL,
    IsClosed          BIT          NOT NULL,
    SortOrder         TINYINT      NOT NULL
);
GO

/* Categoría de estatus / comentario (antes: columna COMMENTS / 'Date - Name:'
   con listas distintas por hoja). Kind = OUTCOME (resultado) o DISCREPANCY
   (causa raíz de un faltante, con área responsable). */
CREATE TABLE cat.Catalog_StatusCategories (
    StatusCategoryId    SMALLINT      NOT NULL CONSTRAINT PK_Catalog_StatusCategories PRIMARY KEY,
    Code                VARCHAR(40)   NOT NULL CONSTRAINT UQ_Catalog_StatusCategories_Code UNIQUE,
    Description         NVARCHAR(150) NOT NULL,
    Kind                VARCHAR(12)   NOT NULL,
    ResponsibleArea     VARCHAR(10)   NULL,
    AffectsDuty         BIT           NOT NULL CONSTRAINT DF_Catalog_StatusCategories_Duty DEFAULT (0),
    IsAutoDerived       BIT           NOT NULL CONSTRAINT DF_Catalog_StatusCategories_Auto DEFAULT (0),
    IsActive            BIT           NOT NULL CONSTRAINT DF_Catalog_StatusCategories_IsActive DEFAULT (1),
    CONSTRAINT CK_Catalog_StatusCategories_Kind CHECK (Kind IN ('OUTCOME','DISCREPANCY'))
);
GO

/* Tipos de orden de reparación en planta (SAP CO01/CO02/COWBHUWE). */
CREATE TABLE cat.Catalog_IbOrderTypes (
    IbOrderTypeCode   CHAR(4)       NOT NULL CONSTRAINT PK_Catalog_IbOrderTypes PRIMARY KEY,  -- 'PPI2'
    Description       NVARCHAR(120) NOT NULL,
    CostBearing       BIT           NOT NULL,
    IsActive          BIT           NOT NULL CONSTRAINT DF_Catalog_IbOrderTypes_IsActive DEFAULT (1)
);
GO

/* Maestro de costos MM60 versionado (reemplaza la hoja oculta 'MM60 COST SAP').
   UnitCostUSD = Price / PriceUnit (misma regla que Table3[Unit Cost MM60]). */
CREATE TABLE cat.Material_Cost_MM60 (
    MaterialCostId    INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_Material_Cost_MM60 PRIMARY KEY,
    Material          VARCHAR(18)   NOT NULL,
    Description       NVARCHAR(80)  NULL,
    Price             DECIMAL(18,4) NOT NULL,
    PriceUnit         INT           NOT NULL CONSTRAINT DF_Material_Cost_MM60_PriceUnit DEFAULT (1),
    UnitCostUSD       AS (CAST(Price / NULLIF(PriceUnit,0) AS DECIMAL(18,6))) PERSISTED,
    Currency          CHAR(3)       NOT NULL CONSTRAINT DF_Material_Cost_MM60_Curr DEFAULT ('USD'),
    BaseUoM           VARCHAR(3)    NULL,
    AbcIndicator      CHAR(1)       NULL,
    ValidFrom         DATE          NOT NULL,
    ValidTo           DATE          NULL,          -- NULL = costo vigente
    Source            VARCHAR(10)   NOT NULL,      -- EXCEL | MM60_FILE | SAP_RFC
    LoadBatchId       UNIQUEIDENTIFIER NULL,
    CONSTRAINT UQ_Material_Cost_MM60 UNIQUE (Material, ValidFrom),
    CONSTRAINT CK_Material_Cost_MM60_PriceUnit CHECK (PriceUnit > 0),
    CONSTRAINT CK_Material_Cost_MM60_Source CHECK (Source IN ('EXCEL','MM60_FILE','SAP_RFC')),
    CONSTRAINT CK_Material_Cost_MM60_Range CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO
CREATE UNIQUE INDEX UX_Material_Cost_MM60_Current
    ON cat.Material_Cost_MM60 (Material) INCLUDE (UnitCostUSD, Description)
    WHERE ValidTo IS NULL;
GO

/* =====================================================================
   2. SEGURIDAD · RBAC por bloque y por Value Stream
   ===================================================================== */
CREATE TABLE sec.Users (
    UserId          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    NtUser          VARCHAR(64)    NOT NULL CONSTRAINT UQ_Users_NtUser UNIQUE,   -- DOMINIO\usuario (AD)
    DisplayName     NVARCHAR(150)  NOT NULL,
    Email           NVARCHAR(200)  NULL,
    DepartmentId    SMALLINT       NULL CONSTRAINT FK_Users_Department REFERENCES cat.Catalog_Departments(DepartmentId),
    IsActive        BIT            NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    IsPlaceholder   BIT            NOT NULL CONSTRAINT DF_Users_IsPlaceholder DEFAULT (0), -- nombre histórico sin NT ID
    CreatedAtUtc    DATETIME2(3)   NOT NULL CONSTRAINT DF_Users_Created DEFAULT (SYSUTCDATETIME())
);
GO

CREATE TABLE sec.App_Roles (
    RoleId        TINYINT       NOT NULL CONSTRAINT PK_App_Roles PRIMARY KEY,
    Code          VARCHAR(20)   NOT NULL CONSTRAINT UQ_App_Roles_Code UNIQUE,
    Name          NVARCHAR(80)  NOT NULL,
    AdGroup       NVARCHAR(128) NULL,     -- grupo AD que otorga el rol (opcional)
    IsAdmin       BIT           NOT NULL CONSTRAINT DF_App_Roles_IsAdmin DEFAULT (0)
);
GO

/* Matriz Champions_VS: qué usuario actúa como Champion/Proxy de qué rol
   (bloque) en qué Value Stream. ValueStreamId NULL = todos los VS. */
CREATE TABLE sec.Champions_Matrix (
    ChampionsMatrixId  INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Champions_Matrix PRIMARY KEY,
    UserId             INT          NOT NULL CONSTRAINT FK_Champions_Matrix_User REFERENCES sec.Users(UserId),
    RoleId             TINYINT      NOT NULL CONSTRAINT FK_Champions_Matrix_Role REFERENCES sec.App_Roles(RoleId),
    ValueStreamId      TINYINT      NULL     CONSTRAINT FK_Champions_Matrix_VS   REFERENCES cat.Catalog_ValueStreams(ValueStreamId),
    DepartmentId       SMALLINT     NULL     CONSTRAINT FK_Champions_Matrix_Dept REFERENCES cat.Catalog_Departments(DepartmentId),
    Assignment         VARCHAR(10)  NOT NULL,
    ValidFrom          DATE         NOT NULL CONSTRAINT DF_Champions_Matrix_From DEFAULT (CAST(GETDATE() AS DATE)),
    ValidTo            DATE         NULL,
    CONSTRAINT CK_Champions_Matrix_Assignment CHECK (Assignment IN ('CHAMPION','PROXY','ADMIN','VIEWER')),
    CONSTRAINT CK_Champions_Matrix_Range CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
    CONSTRAINT UQ_Champions_Matrix UNIQUE (UserId, RoleId, ValueStreamId, ValidFrom)
);
GO

/* Permiso de edición por campo (columna de Pallets_Return_Tracking).
   La API valida cada comando contra esta tabla; el Desktop/Web sólo la
   usa para habilitar/deshabilitar controles. */
CREATE TABLE sec.Field_Permissions (
    RoleId      TINYINT     NOT NULL CONSTRAINT FK_Field_Permissions_Role REFERENCES sec.App_Roles(RoleId),
    FieldName   SYSNAME     NOT NULL,
    StageNo     TINYINT     NOT NULL,
    CanEdit     BIT         NOT NULL CONSTRAINT DF_Field_Permissions_CanEdit DEFAULT (1),
    CONSTRAINT PK_Field_Permissions PRIMARY KEY (RoleId, FieldName),
    CONSTRAINT CK_Field_Permissions_Stage CHECK (StageNo BETWEEN 1 AND 5)
);
GO

/* =====================================================================
   3. PARÁMETROS VERSIONADOS (nada hardcodeado como en el Excel: 0.19,
      DATE(2025,5,31), DATE(2025,10,30), etc.)
   ===================================================================== */
CREATE TABLE cfg.Parameters (
    ParamKey       VARCHAR(50)    NOT NULL,
    ValidFrom      DATE           NOT NULL,
    ValidTo        DATE           NULL,
    NumericValue   DECIMAL(18,6)  NULL,
    TextValue      NVARCHAR(200)  NULL,
    Description    NVARCHAR(300)  NOT NULL,
    ApprovedBy     VARCHAR(64)    NULL,
    CONSTRAINT PK_Parameters PRIMARY KEY (ParamKey, ValidFrom),
    CONSTRAINT CK_Parameters_Range CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO

/* Periodos fiscales (Bosch: año fiscal = año calendario). */
CREATE TABLE cfg.Fiscal_Periods (
    FiscalPeriodId  INT          NOT NULL CONSTRAINT PK_Fiscal_Periods PRIMARY KEY,   -- yyyymm
    FiscalYear      SMALLINT     NOT NULL,
    PeriodNo        TINYINT      NOT NULL,
    Code            VARCHAR(12)  NOT NULL CONSTRAINT UQ_Fiscal_Periods_Code UNIQUE,   -- 'FY2026-P11'
    StartDate       DATE         NOT NULL,
    EndDate         DATE         NOT NULL,
    IsClosed        BIT          NOT NULL CONSTRAINT DF_Fiscal_Periods_IsClosed DEFAULT (0),
    CONSTRAINT CK_Fiscal_Periods_No CHECK (PeriodNo BETWEEN 1 AND 12),
    CONSTRAINT CK_Fiscal_Periods_Range CHECK (EndDate >= StartDate)
);
GO

/* Ventanas de reporte históricas del Excel ('AFTER JUNE 2025',
   '+ NOV 2025', 'FROM OCT 24 TO MAY 25') como datos, no como fórmulas. */
CREATE TABLE cfg.Reporting_Windows (
    WindowCode    VARCHAR(30)    NOT NULL CONSTRAINT PK_Reporting_Windows PRIMARY KEY,
    Description   NVARCHAR(150)  NOT NULL,
    DateBasis     VARCHAR(30)    NOT NULL,   -- columna de fecha que filtra
    StartDate     DATE           NULL,       -- inclusivo
    EndDate       DATE           NULL,       -- inclusivo
    CONSTRAINT CK_Reporting_Windows_Basis CHECK (DateBasis IN ('DateReturnedToWhs','ImportDateElpJup1'))
);
GO

/* =====================================================================
   4. TRANSACCIÓN PRINCIPAL · un registro = un pallet retornado
   ===================================================================== */
CREATE TABLE trk.Folio_Counters (
    ValueStreamId  TINYINT NOT NULL CONSTRAINT PK_Folio_Counters PRIMARY KEY
                   CONSTRAINT FK_Folio_Counters_VS REFERENCES cat.Catalog_ValueStreams(ValueStreamId),
    LastFolio      INT     NOT NULL CONSTRAINT DF_Folio_Counters_Last DEFAULT (0)
);
GO

CREATE TABLE trk.Pallets_Return_Tracking (
    /* ---------- Identidad ---------- */
    PalletReturnId            INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pallets_Return_Tracking PRIMARY KEY,
    ValueStreamId             TINYINT        NOT NULL CONSTRAINT FK_PRT_VS REFERENCES cat.Catalog_ValueStreams(ValueStreamId),
    FolioNumber               INT            NOT NULL,                       -- '#' consecutivo por VS
    FolioCode                 AS (CONCAT('VS', ValueStreamId, '-', RIGHT(CONCAT('00000', FolioNumber), 5))) PERSISTED,

    /* ---------- Etapa 1 · Solicitud y apertura (QMM / solicitante) ---------- */
    QmmResponsibleUserId      INT            NULL CONSTRAINT FK_PRT_QmmUser REFERENCES sec.Users(UserId),
    RequestingDepartmentId    SMALLINT       NULL CONSTRAINT FK_PRT_ReqDept REFERENCES cat.Catalog_Departments(DepartmentId),
    RequestorName             NVARCHAR(150)  NULL,                           -- "Apellido, Nombre" texto libre
    ReasonId                  SMALLINT       NULL CONSTRAINT FK_PRT_Reason REFERENCES cat.Catalog_Reasons(ReasonId),
    ReturnReasonDetail        NVARCHAR(500)  NULL,                           -- '"Return Reason" 22.09.26'
    ReturnedMaterial          VARCHAR(18)    NOT NULL,                       -- P/N 13 dígitos (formato 0285.500.708-24W)
    UnitCostUSD               DECIMAL(18,6)  NULL,                           -- snapshot MM60 al capturar
    UnitCostSource            VARCHAR(10)    NOT NULL CONSTRAINT DF_PRT_CostSource DEFAULT ('PENDING'),
    UnitCostCapturedAtUtc     DATETIME2(3)   NULL,
    QtyImported               INT            NOT NULL,
    SapUsdTotal               AS (CAST(QtyImported * UnitCostUSD AS DECIMAL(18,2))) PERSISTED,
    OriginalHU                VARCHAR(20)    NULL,                           -- 10 dígitos
    OriginalBatch             VARCHAR(10)    NULL,                           -- SAP CHARG

    /* ---------- Etapa 2 · Aduanas y logística de entrada (GS/OSD43, QMM, LOP) ---------- */
    DateReturnedToWhs         DATE           NULL,                           -- 'Date PO' / 'Date Returned to WHS'
    ImportDate                DATE           NULL,
    ImportInvoice             VARCHAR(20)    NULL,                           -- MPSyy000000
    ImportDateElpJup1         DATE           NULL,                           -- cruce fronterizo: abre el ciclo IMMEX
    ImportPedimento           VARCHAR(25)    NULL,
    ReturnPONumber            VARCHAR(10)    NULL,
    ReturnPOItem              VARCHAR(6)     NULL,
    DeliveryNumber            VARCHAR(10)    NULL,
    DutyPaymentStatus         VARCHAR(12)    NULL,

    /* ---------- Etapa 3 · Ejecución en planta (MFO / LOW) ---------- */
    IbOrderNumber             VARCHAR(12)    NULL,
    IbOrderTypeCode           CHAR(4)        NULL CONSTRAINT FK_PRT_IbOrderType REFERENCES cat.Catalog_IbOrderTypes(IbOrderTypeCode),
    PhysicalDeliveredQty      INT            NULL,                           -- 'Physical material delivered to LOW'
    PhysicalDeliveredDate     DATE           NULL,
    NewMaterial               VARCHAR(18)    NULL,                           -- 'Returned Pallet 13 Dgt New PN'
    NewHU                     VARCHAR(20)    NULL,
    ReworkBatch               VARCHAR(10)    NULL,                           -- 'Original Batch' (rework)
    ReinsertionQty            INT            NULL,                           -- material nuevo agregado al pallet
    DateSentBack              DATE           NULL,
    OldMaterialDeliveredQty   INT            NULL,                           -- material IMPORTADO entregado a LOW
    ScrapQty                  INT            NULL,
    ScrapDate                 DATE           NULL,

    /* ---------- Etapa 4 · Cierre aduanal de salida / confirmación de scrap ---------- */
    LowExportNumber           SMALLINT       NULL,                           -- 'expo #4'
    LowExportDate             DATE           NULL,
    LowExportNote             NVARCHAR(200)  NULL,
    CustomsExportReference    VARCHAR(30)    NULL,                           -- 'PR26-00095'
    CustomsExportDate         DATE           NULL,
    ExportPedimento           VARCHAR(25)    NULL,
    ScrapGrNumber             VARCHAR(10)    NULL,
    HseScrapKg                DECIMAL(10,3)  NULL,
    HseConfirmationDate       DATE           NULL,
    GreenConeReported         BIT            NULL,                           -- cono verde
    PlatformReported          BIT            NULL,
    RmaNumber                 VARCHAR(20)    NULL,

    /* ---------- Etapa 5 · Resultados del motor de cálculo (escritos por el Core) ---------- */
    QtyOpenFloor              INT            NOT NULL CONSTRAINT DF_PRT_QtyOpenFloor DEFAULT (0),
    QtyPendingExport          INT            NOT NULL CONSTRAINT DF_PRT_QtyPendExp   DEFAULT (0),
    QtyPendingHse             INT            NOT NULL CONSTRAINT DF_PRT_QtyPendHse   DEFAULT (0),
    QtyMissing                INT            NOT NULL CONSTRAINT DF_PRT_QtyMissing   DEFAULT (0),
    MfoInventoryUSD           DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PRT_MfoInv  DEFAULT (0),
    HseScrapUSD               DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PRT_HseScr  DEFAULT (0),
    MfoLowFailureUSD          DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PRT_MfoLow  DEFAULT (0),
    BalanceGeneralUSD         DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PRT_Balance DEFAULT (0),
    DutyExposureUSD           DECIMAL(18,2)  NOT NULL CONSTRAINT DF_PRT_Duty    DEFAULT (0),
    WorkflowStatusId          TINYINT        NOT NULL CONSTRAINT FK_PRT_Status REFERENCES cat.Catalog_WorkflowStatus(WorkflowStatusId),
    StatusCategoryId          SMALLINT       NULL CONSTRAINT FK_PRT_StatusCat REFERENCES cat.Catalog_StatusCategories(StatusCategoryId),
    IsDutyRisk                BIT            NOT NULL CONSTRAINT DF_PRT_IsDutyRisk DEFAULT (0),
    DutyRiskReason            NVARCHAR(200)  NULL,
    LegalDueDate              DATE           NULL,                           -- ImportDateElpJup1 + plazo legal
    CalcVersion               TINYINT        NOT NULL CONSTRAINT DF_PRT_CalcVersion DEFAULT (2),
    CalculatedAtUtc           DATETIME2(3)   NULL,
    Comments                  NVARCHAR(1000) NULL,

    /* ---------- Control ---------- */
    IsCancelled               BIT            NOT NULL CONSTRAINT DF_PRT_IsCancelled DEFAULT (0),
    CancelReason              NVARCHAR(300)  NULL,
    IsLegacy                  BIT            NOT NULL CONSTRAINT DF_PRT_IsLegacy DEFAULT (0),
    LegacySourceSheet         VARCHAR(31)    NULL,
    LegacySourceRow           INT            NULL,
    DataQualityFlags          VARCHAR(400)   NULL,                           -- códigos DQ-xxx separados por ';'
    CreatedBy                 VARCHAR(64)    NOT NULL,
    CreatedAtUtc              DATETIME2(3)   NOT NULL CONSTRAINT DF_PRT_Created DEFAULT (SYSUTCDATETIME()),
    ModifiedBy                VARCHAR(64)    NULL,
    ModifiedAtUtc             DATETIME2(3)   NULL,
    RowVer                    ROWVERSION     NOT NULL,                       -- concurrencia optimista
    ValidFromUtc              DATETIME2(3)   GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidToUtc                DATETIME2(3)   GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFromUtc, ValidToUtc),

    /* ---------- Reglas de integridad duras (registros nuevos) ---------- */
    CONSTRAINT UQ_PRT_Folio UNIQUE (ValueStreamId, FolioNumber),
    CONSTRAINT CK_PRT_QtyImported   CHECK (QtyImported > 0),
    CONSTRAINT CK_PRT_QtyNonNeg     CHECK (ISNULL(PhysicalDeliveredQty,0) >= 0 AND ISNULL(ReinsertionQty,0) >= 0
                                       AND ISNULL(OldMaterialDeliveredQty,0) >= 0 AND ISNULL(ScrapQty,0) >= 0),
    CONSTRAINT CK_PRT_CostSource    CHECK (UnitCostSource IN ('MM60','SAP_RFC','MANUAL','PENDING','LEGACY')),
    CONSTRAINT CK_PRT_DutyPayStatus CHECK (DutyPaymentStatus IS NULL OR DutyPaymentStatus IN ('NOT_REQUIRED','PENDING','PAID')),
    CONSTRAINT CK_PRT_Material      CHECK (IsLegacy = 1 OR ReturnedMaterial LIKE '[0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z]-[0-9A-Z][0-9A-Z]%'),
    CONSTRAINT CK_PRT_NewMaterial   CHECK (IsLegacy = 1 OR NewMaterial IS NULL OR NewMaterial LIKE '[0-9A-Z][0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z].[0-9A-Z][0-9A-Z][0-9A-Z]-[0-9A-Z][0-9A-Z]%'),
    CONSTRAINT CK_PRT_OriginalHU    CHECK (IsLegacy = 1 OR OriginalHU IS NULL OR (LEN(OriginalHU) = 10 AND OriginalHU NOT LIKE '%[^0-9]%')),
    CONSTRAINT CK_PRT_NewHU         CHECK (IsLegacy = 1 OR NewHU IS NULL OR (LEN(NewHU) = 10 AND NewHU NOT LIKE '%[^0-9]%')),
    /* Conservación de cantidades: lo importado no puede salir dos veces. */
    CONSTRAINT CK_PRT_QtyBalance    CHECK (IsLegacy = 1 OR ISNULL(OldMaterialDeliveredQty,0) + ISNULL(ScrapQty,0) <= QtyImported),
    /* Físico entregado = material original + material reinsertado. */
    CONSTRAINT CK_PRT_PhysicalSplit CHECK (IsLegacy = 1 OR PhysicalDeliveredQty IS NULL OR OldMaterialDeliveredQty IS NULL
                                       OR PhysicalDeliveredQty = OldMaterialDeliveredQty + ISNULL(ReinsertionQty,0)),
    /* Secuencia cronológica del flujo. */
    CONSTRAINT CK_PRT_DateOrder     CHECK (IsLegacy = 1 OR (
                                         (ImportDateElpJup1 IS NULL OR DateReturnedToWhs IS NULL OR ImportDateElpJup1 >= DateReturnedToWhs)
                                     AND (ImportDate IS NULL OR DateReturnedToWhs IS NULL OR ImportDate >= DateReturnedToWhs)
                                     AND (PhysicalDeliveredDate IS NULL OR ImportDateElpJup1 IS NULL OR PhysicalDeliveredDate >= ImportDateElpJup1)
                                     AND (DateSentBack IS NULL OR ImportDateElpJup1 IS NULL OR DateSentBack >= ImportDateElpJup1)
                                     AND (ScrapDate IS NULL OR ImportDateElpJup1 IS NULL OR ScrapDate >= ImportDateElpJup1)
                                     AND (HseConfirmationDate IS NULL OR ScrapDate IS NULL OR HseConfirmationDate >= ScrapDate)
                                     AND (CustomsExportDate IS NULL OR ImportDateElpJup1 IS NULL OR CustomsExportDate >= ImportDateElpJup1))),
    /* Scrap: cantidad > 0 exige fecha; confirmación HSE es todo-o-nada. */
    CONSTRAINT CK_PRT_ScrapDate     CHECK (IsLegacy = 1 OR ISNULL(ScrapQty,0) = 0 OR ScrapDate IS NOT NULL),
    CONSTRAINT CK_PRT_HseBlock      CHECK (IsLegacy = 1 OR
                                         (ScrapGrNumber IS NULL AND HseScrapKg IS NULL AND HseConfirmationDate IS NULL)
                                      OR (ScrapGrNumber IS NOT NULL AND HseScrapKg > 0 AND HseConfirmationDate IS NOT NULL AND ScrapQty > 0)),
    /* Exportación aduanal requiere material entregado a LOW. */
    CONSTRAINT CK_PRT_ExportNeedsQty CHECK (IsLegacy = 1 OR CustomsExportReference IS NULL OR OldMaterialDeliveredQty > 0),
    CONSTRAINT CK_PRT_Cancel        CHECK (IsCancelled = 0 OR CancelReason IS NOT NULL)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = trk.Pallets_Return_Tracking_History, DATA_CONSISTENCY_CHECK = ON));
GO

CREATE INDEX IX_PRT_VS_Status        ON trk.Pallets_Return_Tracking (ValueStreamId, WorkflowStatusId) INCLUDE (BalanceGeneralUSD, IsDutyRisk);
CREATE INDEX IX_PRT_ImportElp        ON trk.Pallets_Return_Tracking (ImportDateElpJup1) INCLUDE (ValueStreamId, BalanceGeneralUSD, WorkflowStatusId);
CREATE INDEX IX_PRT_ReturnedMaterial ON trk.Pallets_Return_Tracking (ReturnedMaterial);
CREATE INDEX IX_PRT_OriginalHU       ON trk.Pallets_Return_Tracking (OriginalHU) WHERE OriginalHU IS NOT NULL;
CREATE INDEX IX_PRT_DutyRisk         ON trk.Pallets_Return_Tracking (LegalDueDate) WHERE IsDutyRisk = 1;
/* Evita doble importación del mismo HU con la misma factura. */
CREATE UNIQUE INDEX UX_PRT_HU_Invoice ON trk.Pallets_Return_Tracking (OriginalHU, ImportInvoice)
    WHERE OriginalHU IS NOT NULL AND ImportInvoice IS NOT NULL AND IsLegacy = 0 AND IsCancelled = 0;
GO

/* Historial de transiciones de estatus (para lead time por etapa en Power BI). */
CREATE TABLE trk.Status_History (
    StatusHistoryId   BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT PK_Status_History PRIMARY KEY,
    PalletReturnId    INT           NOT NULL CONSTRAINT FK_Status_History_PRT REFERENCES trk.Pallets_Return_Tracking(PalletReturnId),
    FromStatusId      TINYINT       NULL     CONSTRAINT FK_Status_History_From REFERENCES cat.Catalog_WorkflowStatus(WorkflowStatusId),
    ToStatusId        TINYINT       NOT NULL CONSTRAINT FK_Status_History_To   REFERENCES cat.Catalog_WorkflowStatus(WorkflowStatusId),
    ChangedBy         VARCHAR(64)   NOT NULL,
    ChangedAtUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_Status_History_At DEFAULT (SYSUTCDATETIME()),
    Note              NVARCHAR(300) NULL
);
CREATE INDEX IX_Status_History_PRT ON trk.Status_History (PalletReturnId, ChangedAtUtc);
GO

/* Alertas generadas por el Worker (aging IMMEX, faltantes, costo pendiente). */
CREATE TABLE trk.Alerts (
    AlertId           BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT PK_Alerts PRIMARY KEY,
    PalletReturnId    INT           NOT NULL CONSTRAINT FK_Alerts_PRT REFERENCES trk.Pallets_Return_Tracking(PalletReturnId),
    AlertType         VARCHAR(20)   NOT NULL,
    Severity          VARCHAR(8)    NOT NULL,
    Message           NVARCHAR(400) NOT NULL,
    RaisedAtUtc       DATETIME2(3)  NOT NULL CONSTRAINT DF_Alerts_Raised DEFAULT (SYSUTCDATETIME()),
    NotifiedTo        NVARCHAR(400) NULL,
    ResolvedAtUtc     DATETIME2(3)  NULL,
    CONSTRAINT CK_Alerts_Type CHECK (AlertType IN ('AGING_WARNING','AGING_CRITICAL','LEGAL_EXPIRED','QTY_MISMATCH','COST_PENDING','EXPORT_SLA','HSE_SLA')),
    CONSTRAINT CK_Alerts_Severity CHECK (Severity IN ('INFO','WARNING','CRITICAL'))
);
CREATE UNIQUE INDEX UX_Alerts_Open ON trk.Alerts (PalletReturnId, AlertType) WHERE ResolvedAtUtc IS NULL;
GO

/* =====================================================================
   5. AUDITORÍA · append-only, nivel campo
   ===================================================================== */
CREATE TABLE aud.Audit_Logs (
    AuditLogId     BIGINT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Audit_Logs PRIMARY KEY,
    TableName      SYSNAME         NOT NULL,
    RecordId       VARCHAR(50)     NOT NULL,
    FolioCode      VARCHAR(20)     NULL,
    Action         CHAR(1)         NOT NULL,   -- I / U / D
    FieldName      SYSNAME         NULL,       -- NULL en Insert/Delete (se guarda JSON completo)
    OldValue       NVARCHAR(4000)  NULL,
    NewValue       NVARCHAR(4000)  NULL,
    NtUser         VARCHAR(64)     NOT NULL,
    ClientApp      VARCHAR(20)     NOT NULL,   -- DESKTOP | WEB | WORKER | MIGRATION | SQL
    ClientMachine  VARCHAR(64)     NULL,
    AppVersion     VARCHAR(20)     NULL,
    CorrelationId  UNIQUEIDENTIFIER NOT NULL,  -- agrupa los campos de un mismo guardado
    ChangeReason   NVARCHAR(500)   NULL,       -- obligatorio en reaperturas/override admin
    ChangedAtUtc   DATETIME2(3)    NOT NULL CONSTRAINT DF_Audit_Logs_At DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Audit_Logs_Action CHECK (Action IN ('I','U','D'))
);
CREATE INDEX IX_Audit_Logs_Record ON aud.Audit_Logs (TableName, RecordId, ChangedAtUtc);
CREATE INDEX IX_Audit_Logs_User   ON aud.Audit_Logs (NtUser, ChangedAtUtc);
GO

/* Bitácora inmutable: ni siquiera la aplicación puede modificarla. */
CREATE OR ALTER TRIGGER aud.TR_Audit_Logs_Immutable
ON aud.Audit_Logs
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'aud.Audit_Logs es append-only: UPDATE/DELETE no permitidos.', 1;
END;
GO

/* =====================================================================
   6. PROCEDIMIENTOS DE SOPORTE
   ===================================================================== */

/* Folio consecutivo por VS, seguro ante concurrencia (sin huecos por rollback
   porque se ejecuta dentro de la transacción del alta). */
CREATE OR ALTER PROCEDURE trk.usp_NextFolio
    @ValueStreamId TINYINT,
    @Folio         INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE trk.Folio_Counters WITH (UPDLOCK, HOLDLOCK)
       SET @Folio = LastFolio = LastFolio + 1
     WHERE ValueStreamId = @ValueStreamId;
    IF @@ROWCOUNT = 0
        THROW 51001, 'Value Stream sin contador de folio.', 1;
END;
GO

/* =====================================================================
   7. ROLES DE BASE DE DATOS (principio de mínimo privilegio)
   ===================================================================== */
IF DATABASE_PRINCIPAL_ID('role_exporeturn_api') IS NULL CREATE ROLE role_exporeturn_api;
IF DATABASE_PRINCIPAL_ID('role_exporeturn_bi')  IS NULL CREATE ROLE role_exporeturn_bi;
GO
GRANT SELECT, INSERT, UPDATE ON SCHEMA::trk TO role_exporeturn_api;
GRANT SELECT                 ON SCHEMA::cat TO role_exporeturn_api;
GRANT SELECT                 ON SCHEMA::sec TO role_exporeturn_api;
GRANT SELECT                 ON SCHEMA::cfg TO role_exporeturn_api;
GRANT SELECT, INSERT         ON SCHEMA::aud TO role_exporeturn_api;
GRANT EXECUTE                ON SCHEMA::trk TO role_exporeturn_api;
DENY  UPDATE, DELETE         ON SCHEMA::aud TO role_exporeturn_api;
DENY  DELETE                 ON SCHEMA::trk TO role_exporeturn_api;   -- se cancela, no se borra
GRANT SELECT                 ON SCHEMA::rpt TO role_exporeturn_bi;    -- Power BI sólo ve vistas
GO
