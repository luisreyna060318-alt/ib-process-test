/* =====================================================================
   Expo and Return FG Follow Up (IB Process) - CHANGE 2026-11-12 (app v0.3.5)
   Requesting departments of the Excel 2.0 list (sheet "Sheet1") offered again, and LOW2 added.
   - Active again: MFQ1, MFQ4, MFQ5, MFQ7, MFQ9, QMM-C1, QMM-C2, PUQ (LOP1 and LOP2 already were).
   - New: LOW2 (area LOW), found in 6 rows of the Excel.
   - The other old codes stay inactive: QMM, MFQ, MFQ2, MFQ3, MFQ6, MFO, MFO4, MFO5, MFO7, MFO9, LOP.
   - Imported records whose department was not in the catalog get it now, taken from their findings
     (DQ-DEPT-NOT-IN-CATALOG:<code>), only if the field is empty. Each one is written to the audit log.
   Nothing is deleted. Run once with Run Script (F5), connected as the owner of the schema.
   ===================================================================== */

SET DEFINE OFF
WHENEVER SQLERROR EXIT ROLLBACK

-- 1. Departments
UPDATE IB_PROCESS_DEPARTMENTS
   SET IS_ACTIVE = 'Y', CAN_REQUEST = 'Y'
 WHERE DEPARTMENT_CODE IN ('MFQ1', 'MFQ4', 'MFQ5', 'MFQ7', 'MFQ9', 'QMM-C1', 'QMM-C2', 'PUQ');

INSERT INTO IB_PROCESS_DEPARTMENTS (DEPARTMENT_CODE, AREA, CAN_REQUEST, DEPARTMENT_GROUP, IS_ACTIVE)
SELECT 'LOW2', 'LOW', 'Y', 'PROCESS', 'Y' FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM IB_PROCESS_DEPARTMENTS WHERE DEPARTMENT_CODE = 'LOW2');

-- 2. Audit log first (who, when, which value), for the records that will receive their department
INSERT INTO IB_PROCESS_AUDIT_LOG
       (TABLE_NAME, RECORD_ID, FOLIO_CODE, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, CLIENT_MACHINE,
        APP_VERSION, CORRELATION_ID, CHANGE_REASON)
SELECT 'IB_PROCESS_PALLET_RETURNS', TO_CHAR(p.PALLET_RETURN_ID), p.FOLIO_CODE, 'I', 'REQUESTING_DEPARTMENT', NULL,
       REGEXP_SUBSTR(p.DATA_QUALITY_FLAGS, 'DQ-DEPT-NOT-IN-CATALOG:([^;]+)', 1, 1, NULL, 1),
       SUBSTR(UPPER(SYS_CONTEXT('USERENV', 'OS_USER')), 1, 64), SUBSTR(SYS_CONTEXT('USERENV', 'HOST'), 1, 64),
       'SQL 2026-11-12', 'CHANGE-2026-11-12-DEPARTMENTS',
       'Department added to the catalog; value taken from the Excel import findings'
  FROM IB_PROCESS_PALLET_RETURNS p
 WHERE p.REQUESTING_DEPARTMENT IS NULL
   AND REGEXP_SUBSTR(p.DATA_QUALITY_FLAGS, 'DQ-DEPT-NOT-IN-CATALOG:([^;]+)', 1, 1, NULL, 1)
       IN (SELECT DEPARTMENT_CODE FROM IB_PROCESS_DEPARTMENTS);

-- 3. The records (ROW_VERSION + 1: a copy open in the app asks to reload instead of overwriting)
UPDATE IB_PROCESS_PALLET_RETURNS p
   SET p.REQUESTING_DEPARTMENT = REGEXP_SUBSTR(p.DATA_QUALITY_FLAGS, 'DQ-DEPT-NOT-IN-CATALOG:([^;]+)', 1, 1, NULL, 1),
       p.MODIFIED_BY = SUBSTR(UPPER(SYS_CONTEXT('USERENV', 'OS_USER')), 1, 64),
       p.MODIFIED_AT = SYSTIMESTAMP,
       p.ROW_VERSION = p.ROW_VERSION + 1
 WHERE p.REQUESTING_DEPARTMENT IS NULL
   AND REGEXP_SUBSTR(p.DATA_QUALITY_FLAGS, 'DQ-DEPT-NOT-IN-CATALOG:([^;]+)', 1, 1, NULL, 1)
       IN (SELECT DEPARTMENT_CODE FROM IB_PROCESS_DEPARTMENTS);

COMMIT;

-- 4. Check: 26 active departments (37 in all), and the LOW2 records
SELECT (SELECT COUNT(*) FROM IB_PROCESS_DEPARTMENTS WHERE IS_ACTIVE = 'Y') AS ACTIVE_26,
       (SELECT COUNT(*) FROM IB_PROCESS_DEPARTMENTS) AS ALL_37
  FROM DUAL;

SELECT FOLIO_CODE, LEGACY_SHEET, LEGACY_ROW, REQUESTING_DEPARTMENT, MODIFIED_BY, MODIFIED_AT
  FROM IB_PROCESS_PALLET_RETURNS
 WHERE DATA_QUALITY_FLAGS LIKE '%DQ-DEPT-NOT-IN-CATALOG:%'
 ORDER BY LEGACY_SHEET, LEGACY_ROW;
