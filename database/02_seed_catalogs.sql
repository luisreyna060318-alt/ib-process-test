/* =====================================================================
   Script 02 · Datos semilla de catálogos, roles, permisos y parámetros
   Fuente: listas de validación encontradas en la MATRIZ DE RASTREO 2.0
   (VS1!BA40:BA49, VS1!AX40:AX47, VS4!BB8:BB16, VS9!BD10:BD17) y la
   especificación funcional. Valores marcados [VALIDAR] requieren
   confirmación del dueño del proceso antes de producción.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- Value Streams ---------- */
INSERT INTO cat.Catalog_ValueStreams (ValueStreamId, Code, Name, LegacySheetName, LegacyTableName, SortOrder) VALUES
 (1, 'VS1', N'Value Stream 1', 'VS1', 'VS_1', 1),
 (4, 'VS4', N'Value Stream 4', 'VS4', 'VS_4', 2),
 (5, 'VS5', N'Value Stream 5', 'VS5', 'VS_5', 3),
 (7, 'VS7', N'Value Stream 7', 'VS7', 'VS_7', 4),
 (9, 'VS9', N'Value Stream 9', 'VS9', 'VS_9', 5);

INSERT INTO trk.Folio_Counters (ValueStreamId, LastFolio)
SELECT ValueStreamId, 0 FROM cat.Catalog_ValueStreams;

/* ---------- Departamentos (padres primero) ---------- */
INSERT INTO cat.Catalog_Departments (Code, Name, FunctionalArea, CanRequest) VALUES
 ('QMM',      N'Quality Management (QMM)',              'QMM', 1),
 ('MFO',      N'Manufacturing Operations',              'MFO', 1),
 ('MFQ',      N'Manufacturing Quality',                 'MFQ', 1),
 ('LOP',      N'Logistics Planning',                    'LOP', 1),
 ('LOW',      N'Logistics Warehouse',                   'LOW', 0),
 ('GS/OSD43', N'Comercio Exterior / Aduanas',           'GS',  0),
 ('HSE',      N'Health, Safety & Environment',          'HSE', 0),
 ('PUQ',      N'Purchasing Quality',                    'PUQ', 1);

DECLARE @QMM SMALLINT = (SELECT DepartmentId FROM cat.Catalog_Departments WHERE Code = 'QMM');
DECLARE @MFO SMALLINT = (SELECT DepartmentId FROM cat.Catalog_Departments WHERE Code = 'MFO');
DECLARE @MFQ SMALLINT = (SELECT DepartmentId FROM cat.Catalog_Departments WHERE Code = 'MFQ');
DECLARE @LOP SMALLINT = (SELECT DepartmentId FROM cat.Catalog_Departments WHERE Code = 'LOP');

INSERT INTO cat.Catalog_Departments (Code, Name, FunctionalArea, ParentDepartmentId, CanRequest)
SELECT CONCAT('MFQ', n), CONCAT(N'Manufacturing Quality ', n), 'MFQ', @MFQ, 1 FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)
UNION ALL
SELECT CONCAT('MFO', n), CONCAT(N'Manufacturing Operations ', n), 'MFO', @MFO, 1 FROM (VALUES (1),(4),(5),(7),(9)) v(n)   -- [VALIDAR] numeración MFO por VS
UNION ALL
SELECT CONCAT('QMM-C', n), CONCAT(N'QMM Customer Quality ', n), 'QMM', @QMM, 1 FROM (VALUES (1),(2)) v(n)
UNION ALL
SELECT CONCAT('LOP', n), CONCAT(N'Logistics Planning ', n), 'LOP', @LOP, 1 FROM (VALUES (1),(2)) v(n);

/* ---------- Motivos oficiales ----------
   NOTA: el Excel contiene 10 motivos (no 9). Se cargan los 10. [VALIDAR] */
INSERT INTO cat.Catalog_Reasons (ReasonId, Code, Description, ReasonGroup, RequiresNewMaterial, SortOrder) VALUES
 ( 1, 'R01', N'Packaging change due to customer request',  'PACKAGING',  0,  1),
 ( 2, 'R02', N'Packaging change due to planning error',    'PACKAGING',  0,  2),
 ( 3, 'R03', N'To change FG P/N with reflash',             'REFLASH',    1,  3),
 ( 4, 'R04', N'To inspect due to possible pallet damage',  'INSPECTION', 0,  4),
 ( 5, 'R05', N'To complete a partial pallet',              'COMPLETION', 0,  5),
 ( 6, 'R06', N'Inspect due to Customer Quality',           'INSPECTION', 0,  6),
 ( 7, 'R07', N'Inspect due to Manufacturing Quality',      'INSPECTION', 0,  7),
 ( 8, 'R08', N'Inspect due to Supplier Quality',           'INSPECTION', 0,  8),
 ( 9, 'R09', N'Inspect due to incomplete box or pallet',   'INSPECTION', 0,  9),
 (10, 'R10', N'Inspect due to mistakes in standard pack',  'PACKAGING',  0, 10);

/* ---------- Estatus del flujo ---------- */
INSERT INTO cat.Catalog_WorkflowStatus (WorkflowStatusId, Code, Name, StageNo, IsClosed, SortOrder) VALUES
 ( 1, 'DRAFT',           N'Borrador (solicitud incompleta)',          1, 0,  1),
 ( 2, 'REQUESTED',       N'Solicitado (pendiente de importación)',    1, 0,  2),
 ( 3, 'IMPORTED',        N'Importado temporalmente (en JuP1)',        2, 0,  3),
 ( 4, 'IN_REWORK',       N'En retrabajo / sorteo (MFO)',              3, 0,  4),
 ( 5, 'PENDING_HSE',     N'Pendiente confirmación de scrap HSE',      4, 0,  5),
 ( 6, 'PENDING_EXPORT',  N'Pendiente exportación aduanal',            4, 0,  6),
 ( 7, 'DUTY_RISK',       N'Riesgo de pago de aranceles',              5, 0,  7),
 ( 8, 'EXPORTED',        N'Cerrado · exportado (y scrap confirmado)', 4, 1,  8),
 ( 9, 'SCRAPPED',        N'Cerrado · 100% destruido (HSE)',           4, 1,  9),
 (10, 'CLOSED_DUTY_PAID',N'Cerrado · discrepancia con duty pagado',   5, 1, 10);

/* ---------- Categorías de estatus = opciones de la columna COMMENTS (texto exacto del Excel) ---------- */
INSERT INTO cat.Catalog_StatusCategories (StatusCategoryId, Code, Kind, ResponsibleArea, AffectsDuty, IsAutoDerived) VALUES
 ( 1, 'EXPORT',                                  'OUTCOME',     NULL,  0, 1),
 ( 2, 'SCRAP',                                   'OUTCOME',     NULL,  0, 1),
 ( 3, 'INVENTORY',                               'OUTCOME',     NULL,  0, 1),
 ( 4, 'ENVIO NACIONAL',                          'OUTCOME',     'GS',  0, 0),
 ( 5, 'SCRAP SIN SALIDA FISICA',                 'DISCREPANCY', 'HSE', 1, 0),
 ( 6, 'NO REPORTADO CORRECTAMENTE',              'DISCREPANCY', NULL,  1, 0),
 ( 7, 'NO REPORTADO CON CONO VERDE',             'DISCREPANCY', 'MFO', 1, 0),
 ( 8, 'NO LLEGO CON CONO',                       'DISCREPANCY', 'LOP', 1, 0),
 ( 9, 'MATERIAL CON CONO NO REPORTADO POR LOW',  'DISCREPANCY', 'LOW', 1, 0),
 (10, 'MATERIAL NO LLEGO DE ELP',                'DISCREPANCY', 'LOP', 1, 0),
 (11, 'LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)',  'DISCREPANCY', 'LOW', 1, 0),
 (12, 'MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)',  'DISCREPANCY', 'MFO', 1, 0);

/* ---------- Opciones por Value Stream (la lista desplegable de cada hoja del Excel) ---------- */
INSERT INTO cat.StatusCategory_ValueStreams (ValueStreamId, StatusCategoryId, SortOrder) VALUES
 (1, 1, 1), (1, 2, 2), (1, 3, 3), (1, 11, 4), (1, 12, 5), (1, 4, 6), (1, 5, 7),
 (4, 6, 1), (4, 1, 2), (4, 2, 3), (4, 3, 4), (4, 7, 5), (4, 8, 6), (4, 9, 7), (4, 4, 8), (4, 10, 9),
 (5, 6, 1), (5, 1, 2), (5, 2, 3), (5, 3, 4), (5, 7, 5), (5, 8, 6), (5, 9, 7),
 (7, 6, 1), (7, 1, 2), (7, 2, 3), (7, 3, 4), (7, 7, 5), (7, 8, 6), (7, 9, 7),
 (9, 6, 1), (9, 1, 2), (9, 2, 3), (9, 3, 4), (9, 7, 5), (9, 8, 6), (9, 9, 7), (9, 4, 8);

/* ---------- Roles de aplicación ---------- */
INSERT INTO sec.App_Roles (RoleId, Code, Name, IsAdmin) VALUES
 ( 1, 'ADMIN_QMM_PE', N'Administrador QMM / Process Excellence', 1),
 ( 2, 'QMM',          N'QMM Champion / Proxy',                   0),
 ( 3, 'REQUESTOR',    N'Área solicitante (MFQ, QMM-C, LOP, PUQ)',0),
 ( 4, 'GS',           N'Comercio Exterior GS/OSD43',             0),
 ( 5, 'LOP',          N'Logística de Planta',                    0),
 ( 6, 'MFO',          N'Manufactura',                            0),
 ( 7, 'LOW',          N'Almacén',                                0),
 ( 8, 'HSE',          N'Seguridad y Medio Ambiente',             0),
 ( 9, 'VIEWER',       N'Consulta (sólo lectura)',                0);

/* ---------- Matriz de permisos por campo (bloques del Excel) ---------- */
INSERT INTO sec.Field_Permissions (RoleId, FieldName, StageNo)
SELECT r.RoleId, f.FieldName, f.StageNo
FROM (VALUES
  -- Etapa 1 · Solicitud: QMM (incluye fecha PO y PO de retorno) y área solicitante
  ('QMM','QmmResponsibleUserId',1),('QMM','RequestingDepartmentId',1),('QMM','RequestorName',1),('QMM','ReasonId',1),
  ('QMM','ReturnReasonDetail',1),('QMM','ReturnedMaterial',1),('QMM','QtyImported',1),('QMM','OriginalHU',1),('QMM','OriginalBatch',1),
  ('QMM','DateReturnedToWhs',1),('QMM','ReturnPONumber',1),
  ('REQUESTOR','RequestingDepartmentId',1),('REQUESTOR','RequestorName',1),('REQUESTOR','ReasonId',1),('REQUESTOR','ReturnReasonDetail',1),
  ('REQUESTOR','ReturnedMaterial',1),('REQUESTOR','QtyImported',1),('REQUESTOR','OriginalHU',1),('REQUESTOR','OriginalBatch',1),
  -- Etapa 2 · Aduana y entrada: GS/OSD43 y LOP
  ('GS','ImportDate',2),('GS','ImportInvoice',2),('GS','ImportDateElpJup1',2),
  ('LOP','DeliveryNumber',2),
  -- Etapa 3 · Ejecución en planta: MFO
  ('MFO','NewMaterial',3),('MFO','PhysicalDeliveredQty',3),('MFO','NewHU',3),('MFO','ReworkBatch',3),('MFO','ReinsertionQty',3),
  ('MFO','DateSentBack',3),('MFO','OldMaterialDeliveredQty',3),('MFO','ScrapQty',3),('MFO','ScrapDate',3),
  -- Etapa 4 · Salida: LOW, GS/OSD43, MFO (GR) y HSE
  ('LOW','LowExport',4),
  ('GS','CustomsExportReference',4),
  ('MFO','ScrapGrNumber',4),
  ('HSE','HseScrapKg',4),('HSE','HseConfirmationDate',4),
  -- Etapa 5 · Categoría de estatus (QMM)
  ('QMM','StatusCategoryId',5)
) f(RoleCode, FieldName, StageNo)
JOIN sec.App_Roles r ON r.Code = f.RoleCode;
/* ADMIN_QMM_PE no necesita filas: IsAdmin = 1 => edición completa (con motivo obligatorio). */

/* ---------- Parámetros ---------- */
INSERT INTO cfg.Parameters (ParamKey, ValidFrom, NumericValue, TextValue, Description) VALUES
 ('DUTY_RATE',                '2024-01-01', 0.19,  NULL, N'Factor de aranceles/impuestos aplicado al valor USD no conciliado (Excel: *0.19). [VALIDAR GS/OSD43]'),
 ('IMMEX_LEGAL_TERM_DAYS',    '2024-01-01', 540,   NULL, N'Plazo legal de permanencia de la importación temporal en días. [VALIDAR GS/OSD43 según régimen/clave de pedimento]'),
 ('ALERT_WARNING_DAYS_LEFT',  '2024-01-01', 60,    NULL, N'Días antes del vencimiento legal para alerta WARNING'),
 ('ALERT_CRITICAL_DAYS_LEFT', '2024-01-01', 30,    NULL, N'Días antes del vencimiento legal para alerta CRITICAL / DUTY_RISK'),
 ('INTERNAL_SLA_REWORK_DAYS', '2024-01-01', 15,    NULL, N'SLA interno: días máximos desde cruce ELP-JuP1 hasta Date Send Back'),
 ('INTERNAL_SLA_EXPORT_DAYS', '2024-01-01', 7,     NULL, N'SLA interno: días máximos desde entrega a LOW hasta exportación aduanal'),
 ('INTERNAL_SLA_HSE_DAYS',    '2024-01-01', 10,    NULL, N'SLA interno: días máximos desde Scrap Date hasta confirmación HSE'),
 ('QTY_TOLERANCE_PCS',        '2024-01-01', 0,     NULL, N'Tolerancia de piezas faltantes antes de marcar discrepancia'),
 ('CALC_VERSION_ACTIVE',      '2024-01-01', 2,     NULL, N'1 = réplica exacta Excel 2.0 (corrida en paralelo); 2 = reglas corregidas');

/* ---------- Ventanas de reporte heredadas del Excel ---------- */
INSERT INTO cfg.Reporting_Windows (WindowCode, Description, DateBasis, StartDate, EndDate) VALUES
 ('LEGACY_OCT24_MAY25', N'FROM OCT 24 TO MAY 25',                 'DateReturnedToWhs', '2024-10-01', '2025-05-31'),
 ('LEGACY_AFTER_JUN25', N'AFTER JUNE 2025 (Date PO > 31-may-2025)','DateReturnedToWhs', '2025-06-01', NULL),
 ('LEGACY_FROM_NOV25',  N'FINISH GOOD RETURN + NOV 2025 (Date PO > 30-oct-2025)', 'DateReturnedToWhs', '2025-10-31', NULL);

/* ---------- Periodos fiscales 2024-2030 ---------- */
;WITH m AS (
    SELECT CAST('2024-01-01' AS DATE) AS d
    UNION ALL SELECT DATEADD(MONTH, 1, d) FROM m WHERE d < '2030-12-01'
)
INSERT INTO cfg.Fiscal_Periods (FiscalPeriodId, FiscalYear, PeriodNo, Code, StartDate, EndDate)
SELECT YEAR(d) * 100 + MONTH(d), YEAR(d), MONTH(d),
       CONCAT('FY', YEAR(d), '-P', RIGHT(CONCAT('0', MONTH(d)), 2)), d, EOMONTH(d)
FROM m
OPTION (MAXRECURSION 200);

COMMIT TRANSACTION;
GO
