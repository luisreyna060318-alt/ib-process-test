/* =====================================================================
   Expo and Return FG Follow Up (IB Process) - Oracle - Script 00: DROP every application object
   Run ONCE, connected as the owner of the schema (JUP1QMM), BEFORE IB_PROCESS_install.sql.
   It deletes the tables below WITH ALL THEIR DATA (no recycle bin: PURGE). Nothing else is touched:
   only the objects named here.

   Tables of v0.4.x (19):
     IB_PROCESS_FEEDBACK, IB_PROCESS_AUDIT_LOG, IB_PROCESS_ALERTS, IB_PROCESS_STATUS_HISTORY,
     IB_PROCESS_PALLET_RETURNS, IB_PROCESS_FOLIO_COUNTERS, IB_PROCESS_PARAMETERS, IB_PROCESS_FIELD_PERMISSIONS,
     IB_PROCESS_REQUEST_VS, IB_PROCESS_PROFILE_REQUESTS, IB_PROCESS_USER_ACCESS, IB_PROCESS_USERS,
     IB_PROCESS_MM60_COSTS, IB_PROCESS_STATUS_CAT_VS, IB_PROCESS_STATUS_CATEGORIES, IB_PROCESS_WORKFLOW_STATUS,
     IB_PROCESS_REASONS, IB_PROCESS_DEPARTMENTS, IB_PROCESS_VALUE_STREAMS
   Tables of v0.5.x (also listed so the script can be run again on a v0.5 schema):
     IB_PROCESS_PALLETS, IB_PROCESS_PROCESSES, IB_PROCESS_COMMENTS
   Views of v0.5.0 (no data): IB_PROCESS_INBOX_V, IB_PROCESS_PALLETS_V, IB_PROCESS_PARAMS_V.
   Schema trigger of v0.4.x that refuses DROP / TRUNCATE: IB_PROCESS_NO_DROP (disabled and dropped first).
   The triggers of each table are dropped with the table. A name that does not exist is skipped.
   ===================================================================== */

SET SERVEROUTPUT ON

-- 1. The schema trigger that refuses DROP must go first (it also protects itself, so: disable, then drop).
BEGIN
    EXECUTE IMMEDIATE 'ALTER TRIGGER IB_PROCESS_NO_DROP DISABLE';
    EXECUTE IMMEDIATE 'DROP TRIGGER IB_PROCESS_NO_DROP';
    DBMS_OUTPUT.PUT_LINE('Dropped trigger IB_PROCESS_NO_DROP');
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE = -4080 THEN DBMS_OUTPUT.PUT_LINE('Trigger IB_PROCESS_NO_DROP does not exist');   -- ORA-04080
        ELSE RAISE;
        END IF;
END;
/

-- 2. The views (they hold no data; the install creates them again).
DECLARE
    TYPE t_names IS TABLE OF VARCHAR2(30);
    v_views t_names := t_names('IB_PROCESS_USER_ACCESS_V', 'IB_PROCESS_INBOX_V', 'IB_PROCESS_PALLETS_V', 'IB_PROCESS_PARAMS_V');
BEGIN
    FOR i IN 1 .. v_views.COUNT LOOP
        BEGIN
            EXECUTE IMMEDIATE 'DROP VIEW ' || v_views(i);
            DBMS_OUTPUT.PUT_LINE('Dropped view ' || v_views(i));
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE = -942 THEN DBMS_OUTPUT.PUT_LINE('View ' || v_views(i) || ' does not exist');   -- ORA-00942
                ELSE RAISE;
                END IF;
        END;
    END LOOP;
END;
/

-- 3. The tables, children first. CASCADE CONSTRAINTS drops foreign keys that point to them; PURGE skips the recycle bin.
DECLARE
    TYPE t_names IS TABLE OF VARCHAR2(30);
    v_tables t_names := t_names(
        'IB_PROCESS_FEEDBACK', 'IB_PROCESS_COMMENTS', 'IB_PROCESS_AUDIT_LOG', 'IB_PROCESS_ALERTS', 'IB_PROCESS_STATUS_HISTORY',
        'IB_PROCESS_PALLETS', 'IB_PROCESS_PALLET_RETURNS', 'IB_PROCESS_PROCESSES', 'IB_PROCESS_FOLIO_COUNTERS',
        'IB_PROCESS_PARAMETERS', 'IB_PROCESS_FIELD_PERMISSIONS', 'IB_PROCESS_REQUEST_VS', 'IB_PROCESS_PROFILE_REQUESTS',
        'IB_PROCESS_USER_ACCESS', 'IB_PROCESS_USERS', 'IB_PROCESS_MM60_COSTS', 'IB_PROCESS_STATUS_CAT_VS',
        'IB_PROCESS_STATUS_CATEGORIES', 'IB_PROCESS_WORKFLOW_STATUS', 'IB_PROCESS_REASONS', 'IB_PROCESS_DEPARTMENTS',
        'IB_PROCESS_VALUE_STREAMS');
BEGIN
    FOR i IN 1 .. v_tables.COUNT LOOP
        BEGIN
            EXECUTE IMMEDIATE 'DROP TABLE ' || v_tables(i) || ' CASCADE CONSTRAINTS PURGE';
            DBMS_OUTPUT.PUT_LINE('Dropped table ' || v_tables(i));
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE = -942 THEN DBMS_OUTPUT.PUT_LINE('Table ' || v_tables(i) || ' does not exist');   -- ORA-00942
                ELSE RAISE;
                END IF;
        END;
    END LOOP;
END;
/

-- 4. Check: must return no rows (no table, view, trigger, index or sequence of the application is left).
SELECT OBJECT_TYPE, OBJECT_NAME
  FROM USER_OBJECTS
 WHERE OBJECT_NAME LIKE 'IB\_PROCESS\_%' ESCAPE '\'
    OR (OBJECT_TYPE IN ('TRIGGER', 'INDEX') AND OBJECT_NAME LIKE 'IB\_%' ESCAPE '\')
 ORDER BY OBJECT_TYPE, OBJECT_NAME;
