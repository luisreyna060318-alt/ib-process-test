/* =====================================================================
   Expo and Return FG Follow Up (IB Process) - Oracle - CHANGE 2026-11-19 (v0.5.5)
   For a database installed with IB_PROCESS_install.sql of v0.5.1 plus the change of 2026-11-18. Run ONCE, connected
   as the owner of the schema (JUP1QMM). A new installation with IB_PROCESS_install.sql already includes it.
   It can run before or after installing the v0.5.5 executable (the application does not read this view).

   1. New view IB_PROCESS_USER_ACCESS_V: Users and Access as a matrix, one row per user and department (active access
      only) with one column per Value Stream holding the function there. It is a view: it stores nothing and does not
      change IB_PROCESS_USER_ACCESS (one row per user x department x VS, with its history), so no data is touched.
   2. The change is written to the audit log as US\RLN8JU.
   3. Check (last query): the view exists, is valid and how many rows it shows.
   ===================================================================== */

SET DEFINE OFF
SET SQLBLANKLINES ON
WHENEVER SQLERROR EXIT ROLLBACK

-- 1. Users and Access matrix (same view as 05_views.sql)
CREATE OR REPLACE VIEW IB_PROCESS_USER_ACCESS_V (NT_USER, DISPLAY_NAME, DEPARTMENT_CODE, IS_OWNER, VS1_FUNCTION, VS4_FUNCTION,
    VS5_FUNCTION, VS7_FUNCTION, VS9_FUNCTION, OTHER_VS_ROWS, ACCESS_ROWS, LAST_CHANGE_AT) AS
SELECT a.NT_USER, u.DISPLAY_NAME, a.DEPARTMENT_CODE,
       MAX(CASE WHEN a.FUNCTION_CODE = 'OWNER' THEN 'Y' ELSE 'N' END),
       MAX(CASE WHEN a.VALUE_STREAM_ID = 1 THEN a.FUNCTION_CODE END),
       MAX(CASE WHEN a.VALUE_STREAM_ID = 4 THEN a.FUNCTION_CODE END),
       MAX(CASE WHEN a.VALUE_STREAM_ID = 5 THEN a.FUNCTION_CODE END),
       MAX(CASE WHEN a.VALUE_STREAM_ID = 7 THEN a.FUNCTION_CODE END),
       MAX(CASE WHEN a.VALUE_STREAM_ID = 9 THEN a.FUNCTION_CODE END),
       SUM(CASE WHEN a.VALUE_STREAM_ID NOT IN (1, 4, 5, 7, 9) THEN 1 ELSE 0 END),
       COUNT(*), MAX(a.VALID_FROM)
  FROM IB_PROCESS_USER_ACCESS a
  JOIN IB_PROCESS_USERS u ON u.NT_USER = a.NT_USER
 WHERE a.VALID_TO IS NULL
 GROUP BY a.NT_USER, u.DISPLAY_NAME, a.DEPARTMENT_CODE;

-- 2. Audit log
INSERT INTO IB_PROCESS_AUDIT_LOG (TABLE_NAME, RECORD_ID, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, APP_VERSION, CHANGE_REASON)
VALUES ('IB_PROCESS_USER_ACCESS', 'CHANGE-2026-11-19', 'I', 'VIEW',
        NULL, 'New view IB_PROCESS_USER_ACCESS_V (Users and Access matrix)',
        'US\RLN8JU', '0.5.5', 'Change script 2026-11-19: readable matrix of the user access for reviews and audits');
COMMIT;

-- 3. Check: must show VALID and the rows of the matrix (one per user and department, plus one per Owner)
SELECT o.OBJECT_NAME, o.STATUS, (SELECT COUNT(*) FROM IB_PROCESS_USER_ACCESS_V) AS MATRIX_ROWS,
       (SELECT COUNT(*) FROM IB_PROCESS_USER_ACCESS WHERE VALID_TO IS NULL) AS ACTIVE_ACCESS_ROWS
  FROM USER_OBJECTS o WHERE o.OBJECT_TYPE = 'VIEW' AND o.OBJECT_NAME = 'IB_PROCESS_USER_ACCESS_V';
