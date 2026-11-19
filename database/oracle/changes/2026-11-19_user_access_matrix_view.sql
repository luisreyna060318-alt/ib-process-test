/* =====================================================================
   Expo and Return FG Follow Up (IB Process) - Oracle - CHANGE 2026-11-19 (v0.5.6)
   For a database installed with IB_PROCESS_install.sql of v0.5.1 plus the change of 2026-11-18. Run it connected as
   the owner of the schema (JUP1QMM), AFTER replacing the executable by v0.5.6 (older executables still read the column
   that part 2 removes). A new installation with IB_PROCESS_install.sql already includes it. It can run more than once.

   1. New view IB_PROCESS_USER_ACCESS_V: Users and Access as a matrix, one row per user and department (active access
      only) with one column per Value Stream holding the function there. A view stores nothing: IB_PROCESS_USER_ACCESS
      keeps one row per user x department x VS with its history.
   2. IB_PROCESS_DEPARTMENTS loses DEPARTMENT_GROUP (PROCESS / CROSS_FUNCTIONAL) and its rule IB_DEPT_CK_GROUP: every
      department is PROCESS and the application no longer distinguishes them. Safety first: if some department is
      not PROCESS the script STOPS before changing anything (that department would have to be reviewed first).
   3. Both changes are written to the audit log as US\RLN8JU (once).
   4. Check (last query): the view is VALID and the column no longer exists (DEPARTMENT_GROUP_COLUMNS = 0).
   ===================================================================== */

SET DEFINE OFF
SET SQLBLANKLINES ON
SET SERVEROUTPUT ON
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
-- 2. Department group: removed only when every department is PROCESS
DECLARE
    v_columns NUMBER;
    v_other   NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_columns FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'IB_PROCESS_DEPARTMENTS' AND COLUMN_NAME = 'DEPARTMENT_GROUP';
    IF v_columns = 0 THEN
        DBMS_OUTPUT.PUT_LINE('DEPARTMENT_GROUP was already removed: nothing to do.');
        RETURN;
    END IF;
    EXECUTE IMMEDIATE 'SELECT COUNT(*) FROM IB_PROCESS_DEPARTMENTS WHERE DEPARTMENT_GROUP <> ''PROCESS''' INTO v_other;
    IF v_other > 0 THEN
        RAISE_APPLICATION_ERROR(-20050, v_other || ' department(s) are not PROCESS: review them before removing DEPARTMENT_GROUP. Nothing was changed.');
    END IF;
    EXECUTE IMMEDIATE 'ALTER TABLE IB_PROCESS_DEPARTMENTS DROP CONSTRAINT IB_DEPT_CK_GROUP';
    EXECUTE IMMEDIATE 'ALTER TABLE IB_PROCESS_DEPARTMENTS DROP COLUMN DEPARTMENT_GROUP';
    DBMS_OUTPUT.PUT_LINE('DEPARTMENT_GROUP and IB_DEPT_CK_GROUP removed.');
END;
/

-- 3. Audit log (each change once, even if the script runs again)
INSERT INTO IB_PROCESS_AUDIT_LOG (TABLE_NAME, RECORD_ID, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, APP_VERSION, CHANGE_REASON)
SELECT 'IB_PROCESS_USER_ACCESS', 'CHANGE-2026-11-19', 'I', 'VIEW', NULL, 'New view IB_PROCESS_USER_ACCESS_V (Users and Access matrix)',
       'US\RLN8JU', '0.5.6', 'Change script 2026-11-19: readable matrix of the user access for reviews and audits'
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM IB_PROCESS_AUDIT_LOG WHERE RECORD_ID = 'CHANGE-2026-11-19' AND FIELD_NAME = 'VIEW');
INSERT INTO IB_PROCESS_AUDIT_LOG (TABLE_NAME, RECORD_ID, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, APP_VERSION, CHANGE_REASON)
SELECT 'IB_PROCESS_DEPARTMENTS', 'CHANGE-2026-11-19', 'D', 'DEPARTMENT_GROUP', 'PROCESS / CROSS_FUNCTIONAL (every department PROCESS)', NULL,
       'US\RLN8JU', '0.5.6', 'Change script 2026-11-19: department group removed, there is no distinction between departments'
  FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM IB_PROCESS_AUDIT_LOG WHERE RECORD_ID = 'CHANGE-2026-11-19' AND FIELD_NAME = 'DEPARTMENT_GROUP');
COMMIT;

-- 4. Check: VALID, the rows of the matrix, and 0 = the department group column no longer exists
SELECT o.OBJECT_NAME, o.STATUS, (SELECT COUNT(*) FROM IB_PROCESS_USER_ACCESS_V) AS MATRIX_ROWS,
       (SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = 'IB_PROCESS_DEPARTMENTS' AND COLUMN_NAME = 'DEPARTMENT_GROUP') AS DEPARTMENT_GROUP_COLUMNS
  FROM USER_OBJECTS o WHERE o.OBJECT_TYPE = 'VIEW' AND o.OBJECT_NAME = 'IB_PROCESS_USER_ACCESS_V';
