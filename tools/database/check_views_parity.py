#!/usr/bin/env python3
"""Runs the SQL of 05_views.sql and of OracleProcessQueries.cs on the data the application wrote, and compares the
result with what the application itself calculates (rows of the inbox, pallets, filters + order, totals, balance).

No Oracle is needed: the Oracle SQL is translated to DuckDB with sqlglot. Only IB_PROCESS_PARAMS_V (Oracle KEEP syntax)
is replaced by its default values, which are the values the seed inserts.

    pip install duckdb sqlglot
    EXPORETURN_SQL_PARITY_DIR=/tmp/parity dotnet test --filter SqlParityDump      (writes data.json / expected.json)
    python3 tools/database/check_views_parity.py /tmp/parity

Exit code 0 = the views and the application agree on every row.
"""
import json, os, re, sys
import duckdb, sqlglot

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DIR = sys.argv[1]
data = json.load(open(os.path.join(DIR, "data.json")))
exp = json.load(open(os.path.join(DIR, "expected.json")))
TODAY = data["QueryDate"]

def nocomments(sql): return re.sub(r"--[^\n]*", "", re.sub(r"/\*.*?\*/", "", sql, flags=re.S))

def to_duck(sql):
    """Oracle -> DuckDB for the few functions the views use, then sqlglot for the rest."""
    sql = sql.replace("TRUNC(SYSDATE)", f"DATE '{TODAY}'")
    sql = re.sub(r"TRUNC\(([\w.]+)\)", r"CAST(\1 AS DATE)", sql)
    sql = re.sub(r"TO_CHAR\(([\w.]+)\)", r"CAST(\1 AS VARCHAR)", sql)
    sql = re.sub(r"EXTRACT\(YEAR FROM ([\w.]+)\)", r"YEAR(\1)", sql)
    sql = re.sub(r":(\w+)", r"$\1", sql)
    return sqlglot.transpile(sql, read="oracle", write="duckdb")[0]

db = duckdb.connect()
db.execute("""CREATE TABLE IB_PROCESS_STATUS_CATEGORIES (STATUS_CATEGORY_ID INTEGER, CODE VARCHAR, AFFECTS_DUTY VARCHAR)""")
db.execute("""CREATE TABLE IB_PROCESS_PROCESSES (PROCESS_ID INTEGER, VALUE_STREAM_ID INTEGER, FOLIO_NUMBER INTEGER, FOLIO_CODE VARCHAR,
    QMM_RESPONSIBLE VARCHAR, REQUESTOR_NAME VARCHAR, REQUESTING_DEPARTMENT VARCHAR, IMPORT_INVOICE VARCHAR,
    ELP_JUP1_CROSSING_DATE DATE, DELIVERY_NUMBER VARCHAR, IS_LEGACY VARCHAR, OPEN_FINDINGS INTEGER)""")
db.execute("""CREATE TABLE IB_PROCESS_PALLETS (PALLET_ID INTEGER, PROCESS_ID INTEGER, LINE_NO INTEGER, RETURNED_PART_NUMBER VARCHAR,
    QTY_IMPORTED INTEGER, MM60_UNIT_COST_USD DECIMAL(18,6), SAP_TOTAL_USD DECIMAL(18,2), ORIGINAL_HU VARCHAR, ORIGINAL_BATCH VARCHAR,
    PO_NUMBER VARCHAR, CUSTOMS_EXPORT VARCHAR, STATUS_CATEGORY_ID INTEGER, NEW_PART_NUMBER VARCHAR, NEW_HU VARCHAR,
    REINSERTION_QTY INTEGER, SCRAP_QTY INTEGER, SENT_BACK_DATE DATE, SCRAP_DATE DATE, QTY_OPEN_FLOOR INTEGER,
    QTY_PENDING_EXPORT INTEGER, QTY_PENDING_HSE INTEGER, QTY_MISSING INTEGER, MFO_INVENTORY_USD DECIMAL(18,2),
    HSE_SCRAP_USD DECIMAL(18,2), MFO_LOW_FAILURE_USD DECIMAL(18,2), GENERAL_BALANCE_USD DECIMAL(18,2), WORKFLOW_STATUS_ID INTEGER,
    IS_LEGACY VARCHAR, DATA_QUALITY_FLAGS VARCHAR)""")
for table, rows in (("IB_PROCESS_STATUS_CATEGORIES", data["Categories"]), ("IB_PROCESS_PROCESSES", data["Processes"]),
                    ("IB_PROCESS_PALLETS", data["Pallets"])):
    cols = list(rows[0].keys())
    db.executemany(f"INSERT INTO {table} ({', '.join(cols)}) VALUES ({', '.join('?' for _ in cols)})", [[r[c] for c in cols] for r in rows])
# Defaults of the parameters (= 02_seed_data.sql); the Oracle view picks the row in force with KEEP (DENSE_RANK LAST).
db.execute("""CREATE VIEW IB_PROCESS_PARAMS_V AS SELECT 540 AS IMMEX_TERM_DAYS, 30 AS CRITICAL_DAYS_LEFT, 60 AS WARNING_DAYS_LEFT,
    0 AS QTY_TOLERANCE_PCS, CAST(0.19 AS DECIMAL(18,6)) AS DUTY_RATE, 15 AS SLA_REWORK_DAYS, 7 AS SLA_EXPORT_DAYS, 10 AS SLA_HSE_DAYS""")

views = nocomments(open(os.path.join(ROOT, "database", "oracle", "05_views.sql"), encoding="utf8").read())
for m in re.finditer(r"CREATE OR REPLACE VIEW (\w+) \(([^)]*)\) AS(.*?);", views, re.S):
    name, cols, body = m.group(1), [c.strip() for c in m.group(2).split(",")], m.group(3)
    if name == "IB_PROCESS_PARAMS_V": continue
    db.execute(f"CREATE VIEW {name} ({', '.join(cols)}) AS {to_duck(body)}")

errors = []
def same(a, b):
    if isinstance(a, (int, float)) or isinstance(b, (int, float)):
        return a is not None and b is not None and abs(float(a) - float(b)) < 0.006
    return (a or None) == (b or None)

def fetch(sql, args=None):
    cur = db.execute(sql, args or {})
    names = [d[0] for d in cur.description]
    return [dict(zip(names, r)) for r in cur.fetchall()]

def d(v): return None if v is None else str(v)[:10]

# 1. Inbox rows
inbox = {r["PROCESS_ID"]: r for r in fetch("SELECT * FROM IB_PROCESS_INBOX_V")}
fields = [("Status", "PROCESS_STATUS_ID"), ("PalletCount", "PALLET_COUNT"), ("Qty", "QTY_IMPORTED"),
          ("FirstPartNumber", "FIRST_PART_NUMBER"), ("PartNumberCount", "PART_NUMBER_COUNT"), ("FirstPoNumber", "FIRST_PO_NUMBER"),
          ("PoNumberCount", "PO_NUMBER_COUNT"), ("SapUsdTotal", "SAP_TOTAL_USD"), ("Balance", "GENERAL_BALANCE_USD"),
          ("DutiesToPayUsd", "DUTIES_TO_PAY_USD"), ("DaysInJuP1", "DAYS_IN_JUP1"), ("DaysToDue", "DAYS_TO_DUE"),
          ("QmmResponsible", "QMM_RESPONSIBLE"), ("ImportInvoice", "IMPORT_INVOICE"), ("OpenFindings", "OPEN_FINDINGS"),
          ("DataQuality", "DATA_QUALITY_FLAGS")]
for e in exp["Rows"]:
    a = inbox.get(e["Id"])
    if a is None: errors.append(f"process {e['FolioCode']} missing in the view"); continue
    for ef, af in fields:
        if not same(e[ef], a[af]): errors.append(f"{e['FolioCode']} {af}: app {e[ef]!r} view {a[af]!r}")
    if (e["IsDutyRisk"]) != (a["IS_DUTY_RISK"] == "Y"): errors.append(f"{e['FolioCode']} IS_DUTY_RISK: app {e['IsDutyRisk']} view {a['IS_DUTY_RISK']}")
    if d(e["NearestDueDate"]) != d(a["NEAREST_DUE_DATE"]): errors.append(f"{e['FolioCode']} NEAREST_DUE_DATE: app {e['NearestDueDate']} view {a['NEAREST_DUE_DATE']}")
if len(inbox) != len(exp["Rows"]): errors.append(f"inbox rows: app {len(exp['Rows'])} view {len(inbox)}")

# 2. Pallets
pal = {r["PALLET_ID"]: r for r in fetch("SELECT * FROM IB_PROCESS_PALLETS_V")}
for e in exp["Pallets"]:
    a = pal[e["Id"]]
    for ef, af in (("Status", "WORKFLOW_STATUS_ID"), ("FolioCode", "PALLET_CODE"), ("Balance", "GENERAL_BALANCE_USD"),
                   ("DaysToDue", "DAYS_TO_DUE"), ("StatusCategoryCode", "STATUS_CATEGORY_CODE")):
        if not same(e[ef], a[af]): errors.append(f"pallet {e['FolioCode']} {af}: app {e[ef]!r} view {a[af]!r}")
    if d(e["LegalDueDate"]) != d(a["LEGAL_DUE_DATE"]): errors.append(f"pallet {e['FolioCode']} LEGAL_DUE_DATE: app {e['LegalDueDate']} view {a['LEGAL_DUE_DATE']}")

# 3. Filters and order of the application (SQL built by OracleProcessQueries.Where / OrderBy)
code = open(os.path.join(ROOT, "src", "ExpoReturn.Infrastructure.Oracle", "OracleProcessQueries.cs"), encoding="utf8").read()
def code_sql(method):
    body = code[code.index(f" {method}("):]
    return re.search(r'_db\.Query\(\$@"(.*?)",\s*r =>', body, re.S).group(1)
totals_sql = code_sql("Totals")
for qd in exp["Queries"]:
    where = qd["Where"]
    ids = [r["PROCESS_ID"] for r in fetch(to_duck(f"SELECT i.PROCESS_ID FROM IB_PROCESS_INBOX_V i WHERE {where} ORDER BY {qd['OrderBy']}"), qd["Args"])]
    if ids != qd["Ids"]:
        first = next((k for k, (x, y) in enumerate(zip(ids, qd["Ids"])) if x != y), min(len(ids), len(qd["Ids"])))
        errors.append(f"query {qd['Name']}: view {len(ids)} ids, app {len(qd['Ids'])}; first difference at position {first}")
    t = fetch(to_duck(totals_sql.replace("{Inbox}", "IB_PROCESS_INBOX_V").replace("{where}", where)), qd["Args"])[0]
    et = qd["Totals"]
    for ef, af in (("Count", "PROCESS_COUNT"), ("Open", "OPEN_COUNT"), ("DutyRisk", "RISK_COUNT"), ("Balance", "BALANCE_USD")):
        if not same(et[ef], t[af]): errors.append(f"totals {qd['Name']} {af}: app {et[ef]} view {t[af]}")

# 4. Balance / Duties and status distribution
bal_sql = code_sql("Balance").replace("{PalletsView}", "IB_PROCESS_PALLETS_V")
for key, year in (("Balance", None), ("Balance2026", 2026)):
    sql = bal_sql.replace("{year}", "" if year is None else " AND EXTRACT(YEAR FROM v.CROSSING_DATE) = :year")
    rows = {f"VS{r['VALUE_STREAM_ID']}": r for r in fetch(to_duck(sql), {} if year is None else {"year": year})}
    for e in exp[key]:
        if e["ValueStream"] == "Total": continue
        a = rows.get(e["ValueStream"])
        if a is None: errors.append(f"{key} {e['ValueStream']} missing"); continue
        for ef, af in (("Pallets", "PALLET_COUNT"), ("OpenPallets", "OPEN_COUNT"), ("DutyRiskPallets", "RISK_COUNT"),
                       ("TotalImportedUsd", "TOTAL_USD"), ("MfoInventoryUsd", "INVENTORY_USD"), ("HseScrapUsd", "SCRAP_USD"),
                       ("MfoLowFailureUsd", "FAILURE_USD"), ("DutiesToPayUsd", "DUTIES_USD")):
            if not same(e[ef], a[af]): errors.append(f"{key} {e['ValueStream']} {af}: app {e[ef]} view {a[af]}")
st = {r["PROCESS_STATUS_ID"]: r["PROCESS_COUNT"] for r in fetch(to_duck(code_sql("StatusDistribution").replace("{Inbox}", "IB_PROCESS_INBOX_V")))}
for e in exp["Status"]:
    if st.get(e["Status"]) != e["Count"]: errors.append(f"status {e['Status']}: app {e['Count']} view {st.get(e['Status'])}")

risk = sum(1 for r in exp["Rows"] if r["IsDutyRisk"])
print(f"{len(exp['Rows'])} processes, {len(exp['Pallets'])} pallets, {len(exp['Queries'])} filter/order queries, "
      f"{risk} at duty risk on {TODAY} (stored on {data['SaveDate']})")
for e in errors[:40]: print("  DIFF", e)
print("OK: the views and the application agree" if not errors else f"{len(errors)} differences")
sys.exit(1 if errors else 0)
