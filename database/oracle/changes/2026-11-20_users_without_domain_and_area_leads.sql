/* =====================================================================
   Expo and Return FG Follow Up (IB Process) - Oracle - CHANGE 2026-11-20 (v0.5.7)
   For a database installed with IB_PROCESS_install.sql (v0.5.1 or later). Run it connected as the owner of the schema
   (JUP1QMM), together with the change to the v0.5.7 executable (that version keeps the Windows user WITHOUT domain).
   A new installation with IB_PROCESS_install.sql already stores users without domain. It can run more than once.

   1. Users without domain: 'US\RLN8JU' -> 'RLN8JU' in IB_PROCESS_USERS and in every table that points to the user
      (accesses, profile requests, QMM Responsible and Requestor of the processes). Safety first: if the short name
      already exists (two users would become one) the script STOPS and nothing is changed.
   2. Same change in the columns that only keep who did something (assigned / removed / decided / approved by, created
      and modified by, status history, alerts handled by, feedback). NOT in the audit log nor in the comments: they are
      never edited, they keep the user as it was written then (the application compares both forms as the same user).
   3. Champions and proxies of the areas (each access = one row user x department x VS, as the application writes
      them). A user that does not exist is created; if the same user already has another function in that department
      and VS, that row is closed with date (kept as history) and the new one is opened. Each change goes to the audit log
      as RLN8JU.
         VS1: MFO Champion BAJ1JU, MFO Proxy LZJ3JU, QMM Champion WGE1JU
         VS1, VS4, VS5, VS7, VS9: LOW Champion OVG1JU, LOW Proxies GOD2JU and HAD1JU,
                                  OSD Champion AVS2JU, OSD Proxy ROC2JU2
   4. Rule IB_USR_CK_NO_DOMAIN: from now on the database refuses a user with domain.
   5. Checks (last queries): users with domain = 0, and the active accesses of every user.
   ===================================================================== */

SET DEFINE OFF
SET SQLBLANKLINES ON
SET SERVEROUTPUT ON
WHENEVER SQLERROR EXIT ROLLBACK

-- 1. Users without domain (new row, references moved, old row removed: the user key is pointed to by foreign keys)
DECLARE
    v_exists NUMBER;
    v_moved  NUMBER := 0;
BEGIN
    FOR r IN (SELECT NT_USER, SUBSTR(NT_USER, INSTR(NT_USER, '\', -1) + 1) AS SHORT_USER
                FROM IB_PROCESS_USERS WHERE INSTR(NT_USER, '\') > 0 ORDER BY NT_USER) LOOP
        SELECT COUNT(*) INTO v_exists FROM IB_PROCESS_USERS WHERE NT_USER = r.SHORT_USER;
        IF v_exists > 0 THEN
            RAISE_APPLICATION_ERROR(-20060, r.NT_USER || ' and ' || r.SHORT_USER || ' both exist: they would become one user. Nothing was changed.');
        END IF;
        INSERT INTO IB_PROCESS_USERS (NT_USER, DISPLAY_NAME, DEPARTMENT_CODE, EMAIL, IS_ACTIVE, CREATED_AT)
            SELECT r.SHORT_USER, DISPLAY_NAME, DEPARTMENT_CODE, EMAIL, IS_ACTIVE, CREATED_AT FROM IB_PROCESS_USERS WHERE NT_USER = r.NT_USER;
        UPDATE IB_PROCESS_USER_ACCESS SET NT_USER = r.SHORT_USER WHERE NT_USER = r.NT_USER;
        UPDATE IB_PROCESS_PROFILE_REQUESTS SET NT_USER = r.SHORT_USER WHERE NT_USER = r.NT_USER;
        UPDATE IB_PROCESS_PROCESSES SET QMM_RESPONSIBLE_NT = r.SHORT_USER WHERE QMM_RESPONSIBLE_NT = r.NT_USER;
        UPDATE IB_PROCESS_PROCESSES SET REQUESTOR_NT = r.SHORT_USER WHERE REQUESTOR_NT = r.NT_USER;
        DELETE FROM IB_PROCESS_USERS WHERE NT_USER = r.NT_USER;
        DBMS_OUTPUT.PUT_LINE(r.NT_USER || ' -> ' || r.SHORT_USER);
        v_moved := v_moved + 1;
    END LOOP;
    IF v_moved > 0 THEN
        INSERT INTO IB_PROCESS_AUDIT_LOG (TABLE_NAME, RECORD_ID, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, APP_VERSION, CHANGE_REASON)
        VALUES ('IB_PROCESS_USERS', 'CHANGE-2026-11-20', 'U', 'NT_USER', 'DOMAIN\USER',
                v_moved || ' user(s) stored without domain', 'RLN8JU', '0.5.7', 'Change script 2026-11-20: Windows users without domain');
    END IF;
    DBMS_OUTPUT.PUT_LINE(v_moved || ' user(s) changed to the account without domain.');
END;
/

-- 2. Columns that keep who did something (no foreign key)
UPDATE IB_PROCESS_USER_ACCESS SET ASSIGNED_BY = SUBSTR(ASSIGNED_BY, INSTR(ASSIGNED_BY, '\', -1) + 1) WHERE INSTR(ASSIGNED_BY, '\') > 0;
UPDATE IB_PROCESS_USER_ACCESS SET REMOVED_BY = SUBSTR(REMOVED_BY, INSTR(REMOVED_BY, '\', -1) + 1) WHERE INSTR(REMOVED_BY, '\') > 0;
UPDATE IB_PROCESS_PROFILE_REQUESTS SET DECIDED_BY = SUBSTR(DECIDED_BY, INSTR(DECIDED_BY, '\', -1) + 1) WHERE INSTR(DECIDED_BY, '\') > 0;
UPDATE IB_PROCESS_PARAMETERS SET APPROVED_BY = SUBSTR(APPROVED_BY, INSTR(APPROVED_BY, '\', -1) + 1) WHERE INSTR(APPROVED_BY, '\') > 0;
UPDATE IB_PROCESS_PROCESSES SET CREATED_BY = SUBSTR(CREATED_BY, INSTR(CREATED_BY, '\', -1) + 1) WHERE INSTR(CREATED_BY, '\') > 0;
UPDATE IB_PROCESS_PROCESSES SET MODIFIED_BY = SUBSTR(MODIFIED_BY, INSTR(MODIFIED_BY, '\', -1) + 1) WHERE INSTR(MODIFIED_BY, '\') > 0;
UPDATE IB_PROCESS_PALLETS SET CREATED_BY = SUBSTR(CREATED_BY, INSTR(CREATED_BY, '\', -1) + 1) WHERE INSTR(CREATED_BY, '\') > 0;
UPDATE IB_PROCESS_PALLETS SET MODIFIED_BY = SUBSTR(MODIFIED_BY, INSTR(MODIFIED_BY, '\', -1) + 1) WHERE INSTR(MODIFIED_BY, '\') > 0;
UPDATE IB_PROCESS_STATUS_HISTORY SET CHANGED_BY = SUBSTR(CHANGED_BY, INSTR(CHANGED_BY, '\', -1) + 1) WHERE INSTR(CHANGED_BY, '\') > 0;
UPDATE IB_PROCESS_ALERTS SET HANDLED_BY = SUBSTR(HANDLED_BY, INSTR(HANDLED_BY, '\', -1) + 1) WHERE INSTR(HANDLED_BY, '\') > 0;
UPDATE IB_PROCESS_FEEDBACK SET NT_USER = SUBSTR(NT_USER, INSTR(NT_USER, '\', -1) + 1) WHERE INSTR(NT_USER, '\') > 0;

-- 3. Champions and proxies of the areas
DECLARE
    TYPE t_lead IS RECORD (nt VARCHAR2(64), person NVARCHAR2(150), dept VARCHAR2(20), fn VARCHAR2(10), vs NUMBER);
    TYPE t_leads IS TABLE OF t_lead;
    TYPE t_numbers IS TABLE OF NUMBER;
    v_leads   t_leads := t_leads();
    v_all_vs  t_numbers := t_numbers(1, 4, 5, 7, 9);
    v_nt      VARCHAR2(64);
    v_person  NVARCHAR2(150);
    v_dept    VARCHAR2(20);
    v_fn      VARCHAR2(10);
    v_vs      NUMBER;
    v_current VARCHAR2(10);
    v_added   NUMBER := 0;
    PROCEDURE put_lead(p_nt VARCHAR2, p_person NVARCHAR2, p_dept VARCHAR2, p_fn VARCHAR2, p_vs NUMBER) IS
    BEGIN
        v_leads.EXTEND;
        v_leads(v_leads.COUNT).nt := p_nt;
        v_leads(v_leads.COUNT).person := p_person;
        v_leads(v_leads.COUNT).dept := p_dept;
        v_leads(v_leads.COUNT).fn := p_fn;
        v_leads(v_leads.COUNT).vs := p_vs;
    END;
BEGIN
    put_lead('BAJ1JU', N'Barrera Barrera Jose Ramon', 'MFO', 'CHAMPION', 1);
    put_lead('LZJ3JU', N'Lopez Pinedo Juan Antonio', 'MFO', 'PROXY', 1);
    put_lead('WGE1JU', N'Wong Villalobos Enrique Alberto', 'QMM', 'CHAMPION', 1);
    FOR i IN 1 .. v_all_vs.COUNT LOOP
        put_lead('OVG1JU', N'Oviedo Vega Jesus Guadalupe', 'LOW', 'CHAMPION', v_all_vs(i));
        put_lead('GOD2JU', N'Gonzalez Vergara David', 'LOW', 'PROXY', v_all_vs(i));
        put_lead('HAD1JU', N'Herrera Martinez David', 'LOW', 'PROXY', v_all_vs(i));
        put_lead('AVS2JU', N'Avila Alfaro Cynthia Nallely', 'OSD', 'CHAMPION', v_all_vs(i));
        put_lead('ROC2JU2', N'Roldan Castanon Carlos Ivan', 'OSD', 'PROXY', v_all_vs(i));
    END LOOP;
    FOR i IN 1 .. v_leads.COUNT LOOP
        v_nt := v_leads(i).nt;
        v_person := v_leads(i).person;
        v_dept := v_leads(i).dept;
        v_fn := v_leads(i).fn;
        v_vs := v_leads(i).vs;
        INSERT INTO IB_PROCESS_USERS (NT_USER, DISPLAY_NAME, DEPARTMENT_CODE)
            SELECT v_nt, v_person, v_dept FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM IB_PROCESS_USERS WHERE NT_USER = v_nt);
        UPDATE IB_PROCESS_USERS SET DEPARTMENT_CODE = v_dept WHERE NT_USER = v_nt AND DEPARTMENT_CODE IS NULL;
        SELECT MAX(FUNCTION_CODE) INTO v_current FROM IB_PROCESS_USER_ACCESS
         WHERE NT_USER = v_nt AND DEPARTMENT_CODE = v_dept AND VALUE_STREAM_ID = v_vs AND VALID_TO IS NULL;
        IF v_current IS NULL OR v_current <> v_fn THEN
            UPDATE IB_PROCESS_USER_ACCESS SET VALID_TO = SYSTIMESTAMP, REMOVED_BY = 'RLN8JU'
             WHERE NT_USER = v_nt AND DEPARTMENT_CODE = v_dept AND VALUE_STREAM_ID = v_vs AND VALID_TO IS NULL;
            INSERT INTO IB_PROCESS_USER_ACCESS (NT_USER, DEPARTMENT_CODE, VALUE_STREAM_ID, FUNCTION_CODE, ASSIGNED_BY)
            VALUES (v_nt, v_dept, v_vs, v_fn, 'RLN8JU');
            INSERT INTO IB_PROCESS_AUDIT_LOG (TABLE_NAME, RECORD_ID, ACTION, FIELD_NAME, OLD_VALUE, NEW_VALUE, NT_USER, APP_VERSION, CHANGE_REASON)
            VALUES ('IB_PROCESS_USER_ACCESS', v_nt, CASE WHEN v_current IS NULL THEN 'I' ELSE 'U' END, 'FUNCTION_CODE',
                    CASE WHEN v_current IS NULL THEN NULL ELSE v_dept || ' VS' || v_vs || ' ' || v_current END,
                    v_dept || ' VS' || v_vs || ' ' || v_fn,
                    'RLN8JU', '0.5.7', 'Change script 2026-11-20: champions and proxies of the areas');
            v_added := v_added + 1;
        END IF;
    END LOOP;
    DBMS_OUTPUT.PUT_LINE(v_added || ' access row(s) added or changed (28 the first time).');
END;
/
COMMIT;

-- 4. From now on the database refuses users with domain
DECLARE
    v_exists NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_exists FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME = 'IB_USR_CK_NO_DOMAIN';
    IF v_exists = 0 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE IB_PROCESS_USERS ADD CONSTRAINT IB_USR_CK_NO_DOMAIN CHECK (INSTR(NT_USER, ''\'') = 0)';
        DBMS_OUTPUT.PUT_LINE('Rule IB_USR_CK_NO_DOMAIN added.');
    END IF;
END;
/

-- 5. Checks: USERS_WITH_DOMAIN_0 = 0 and RULE_1 = 1; then the active accesses (function per VS)
SELECT (SELECT COUNT(*) FROM IB_PROCESS_USERS WHERE INSTR(NT_USER, '\') > 0) AS USERS_WITH_DOMAIN_0,
       (SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE CONSTRAINT_NAME = 'IB_USR_CK_NO_DOMAIN') AS RULE_1
  FROM DUAL;
SELECT a.NT_USER, u.DISPLAY_NAME, NVL(a.DEPARTMENT_CODE, '-') AS DEPARTMENT_CODE, a.FUNCTION_CODE,
       LISTAGG(NVL(TO_CHAR(a.VALUE_STREAM_ID), 'all'), ', ') WITHIN GROUP (ORDER BY a.VALUE_STREAM_ID) AS VALUE_STREAMS
  FROM IB_PROCESS_USER_ACCESS a
  JOIN IB_PROCESS_USERS u ON u.NT_USER = a.NT_USER
 WHERE a.VALID_TO IS NULL
 GROUP BY a.NT_USER, u.DISPLAY_NAME, a.DEPARTMENT_CODE, a.FUNCTION_CODE
 ORDER BY 3, 4, 1;
