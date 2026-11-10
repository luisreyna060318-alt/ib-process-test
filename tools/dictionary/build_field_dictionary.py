#!/usr/bin/env python3
"""
Generates entregables/Diccionario_Campos_ExpoReturn.xlsx: dictionary between the name of each field in the
application, its Oracle column and the header / column it had in the Excel MATRIZ DE RASTREO 2.0
(sheets VS1, VS4, VS5, VS7 and VS9). Fields come from tools/dictionary/fields.py.

    python3 tools/dictionary/build_field_dictionary.py

If a name changes in the app (src/ExpoReturn.Desktop/FieldHelp.cs) or in Oracle (database/oracle/01_create_tables.sql),
update tools/dictionary/fields.py and regenerate.
"""
import datetime as dt
import os

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "entregables", "Diccionario_Campos_ExpoReturn.xlsx")

FONT = "Arial"
NAVY = "1F3864"
LIGHT = "EEF2F7"
CALC = "F3F4F6"
NEW = "FFF4E5"

import sys
sys.path.insert(0, os.path.dirname(__file__))
from fields import FIELDS  # noqa: E402  (shared with the Oracle comment generator)

# Location of the "DUTIES TO PAY USD" block (AFTER JUNE 2025) in each sheet
DUTIES_COL = {"VS1": None, "VS4": "AW", "VS5": "AY", "VS7": "AX", "VS9": "AT"}

COMMENTS = [  # exact text, kind, responsible area, affects duty, automatic
    ("EXPORT", "Outcome", "—", "No", "Yes"),
    ("SCRAP", "Outcome", "—", "No", "Yes"),
    ("INVENTORY", "Outcome", "—", "No", "Yes"),
    ("ENVIO NACIONAL", "Outcome", "GS/OSD43", "No", "No"),
    ("SCRAP SIN SALIDA FISICA", "Discrepancy", "HSE", "Yes", "No"),
    ("NO REPORTADO CORRECTAMENTE", "Discrepancy", "—", "Yes", "No"),
    ("NO REPORTADO CON CONO VERDE", "Discrepancy", "MFO", "Yes", "No"),
    ("NO LLEGO CON CONO", "Discrepancy", "LOP", "Yes", "No"),
    ("MATERIAL CON CONO NO REPORTADO POR LOW", "Discrepancy", "LOW", "Yes", "No"),
    ("MATERIAL NO LLEGO DE ELP", "Discrepancy", "LOP", "Yes", "No"),
    ("LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "Discrepancy", "LOW", "Yes", "No"),
    ("MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)", "Discrepancy", "MFO", "Yes", "No"),
]
COMMENTS_BY_VS = {
    "VS1": ["EXPORT", "SCRAP", "INVENTORY", "LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)",
            "ENVIO NACIONAL", "SCRAP SIN SALIDA FISICA"],
    "VS4": ["NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
            "MATERIAL CON CONO NO REPORTADO POR LOW", "ENVIO NACIONAL", "MATERIAL NO LLEGO DE ELP"],
    "VS5": ["NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
            "MATERIAL CON CONO NO REPORTADO POR LOW"],
    "VS7": ["NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
            "MATERIAL CON CONO NO REPORTADO POR LOW"],
    "VS9": ["NO REPORTADO CORRECTAMENTE", "EXPORT", "SCRAP", "INVENTORY", "NO REPORTADO CON CONO VERDE", "NO LLEGO CON CONO",
            "MATERIAL CON CONO NO REPORTADO POR LOW", "ENVIO NACIONAL"],
}

TABLES = [
    ("IB_PROCESS_PALLET_RETURNS", "One row per returned pallet: every field of the 'Dictionary' sheet.", "One row of a VS sheet"),
    ("IB_PROCESS_FOLIO_COUNTERS", "Last folio per Value Stream.", "Column #"),
    ("IB_PROCESS_STATUS_HISTORY", "Calculated status changes (time in each stage).", "Not in the Excel"),
    ("IB_PROCESS_ALERTS", "Alerts (IMMEX term, missing pieces, internal deadlines) and how they were handled.", "Not in the Excel"),
    ("IB_PROCESS_USERS", "Users identified by their Windows account (DOMAIN\\USER), name and profile department.", "Names typed by hand"),
    ("IB_PROCESS_USER_ACCESS", "User / Department / Value Stream / Function (VIEWER, PROXY, CHAMPION, OWNER). Owner rows have no department or VS.", "Sheet 'Champions VS'"),
    ("IB_PROCESS_PROFILE_REQUESTS", "Department / VS changes asked from 'My profile', approved or rejected by the Owner.", "Not in the Excel"),
    ("IB_PROCESS_REQUEST_VS", "Value Streams of each profile request.", "Not in the Excel"),
    ("IB_PROCESS_FIELD_PERMISSIONS", "Fields each area edits.", "Area row above the headers"),
    ("IB_PROCESS_VALUE_STREAMS", "VS1, VS4, VS5, VS7, VS9.", "The 5 VS sheets"),
    ("IB_PROCESS_DEPARTMENTS", "Department codes and the area each belongs to (MFQ4 → MFQ).", "Requesting department"),
    ("IB_PROCESS_REASONS", "The 10 reasons (Excel text).", "'Reason' list"),
    ("IB_PROCESS_STATUS_CATEGORIES", "Status category options with their exact text, kind and responsible area.", "COMMENTS lists"),
    ("IB_PROCESS_STATUS_CAT_VS", "Which options each VS offers and in which order.", "COMMENTS drop-down of each sheet"),
    ("IB_PROCESS_WORKFLOW_STATUS", "The 10 calculated statuses.", "Not in the Excel"),
    ("IB_PROCESS_MM60_COSTS", "Cost master: material, description, price, price unit, currency. Loaded by the Owner with Import MM60 file (SAP export).", "Hidden sheet 'MM60 COST SAP'"),
    ("IB_PROCESS_PARAMETERS", "Duty rate (0.19), IMMEX term (540 days) and alert thresholds; both values to validate with GS/OSD43.", "Fixed numbers inside formulas"),
    ("IB_PROCESS_AUDIT_LOG", "Audit log: who changed which field, when, old and new value (append-only).", "Not in the Excel"),
    ("IB_PROCESS_FEEDBACK", "Reviewer feedback ('Send feedback' button).", "Not in the Excel"),
]


def sheet_col(sheet, vs1_index):
    """Letra de columna de un campo en cada hoja. VS4 = VS1; VS5 tiene una columna extra ('.') después de PO;
    VS7 está recorrida una a la derecha; VS9 una a la izquierda."""
    if vs1_index is None:
        return "—"
    if vs1_index == "DUTIES":
        return DUTIES_COL[sheet] or "—"
    idx = {"VS1": vs1_index, "VS4": vs1_index, "VS5": vs1_index if vs1_index <= 18 else vs1_index + 1,
           "VS7": vs1_index + 1, "VS9": vs1_index - 1}[sheet]
    return get_column_letter(idx)


def style_header(ws, row, ncols):
    for c in range(1, ncols + 1):
        cell = ws.cell(row, c)
        cell.font = Font(name=FONT, bold=True, color="FFFFFF", size=10)
        cell.fill = PatternFill("solid", fgColor=NAVY)
        cell.alignment = Alignment(vertical="center", wrap_text=True)
    ws.row_dimensions[row].height = 32


def body(ws, first_row, last_row, ncols):
    thin = Side(style="thin", color="D5DBE5")
    for r in range(first_row, last_row + 1):
        for c in range(1, ncols + 1):
            cell = ws.cell(r, c)
            cell.font = Font(name=FONT, size=10, bold=cell.font.bold)
            cell.alignment = Alignment(vertical="top", wrap_text=True)
            cell.border = Border(bottom=thin)


def title(ws, text, subtitle):
    ws["A1"] = text
    ws["A1"].font = Font(name=FONT, bold=True, size=14, color=NAVY)
    ws["A2"] = subtitle
    ws["A2"].font = Font(name=FONT, italic=True, size=10, color="555555")


def main():
    wb = Workbook()

    # ---- Sheet 1 · Dictionary ----
    ws = wb.active
    ws.title = "Dictionary"
    title(ws, "Expo & Return FG Follow Up · Field dictionary",
          f"Name in the application ↔ Oracle column ↔ Excel MATRIZ DE RASTREO 2.0 column · generated {dt.date.today():%d/%m/%Y}")
    headers = ["#", "Stage / tab", "Name in the application", "Excel 2.0 header",
               "Col. VS1", "Col. VS4", "Col. VS5", "Col. VS7", "Col. VS9",
               "Oracle column (IB_PROCESS_PALLET_RETURNS)", "Oracle type", "Property in the code", "Captured by",
               "Capture / calculated", "Rule or description"]
    hr = 4
    for i, h in enumerate(headers, 1):
        ws.cell(hr, i, h)
    style_header(ws, hr, len(headers))
    r = hr + 1
    for n, (stage, label, code, db, sqltype, excel, idx, area, origin, rule) in enumerate(FIELDS, 1):
        values = [n, stage, label, excel] + [sheet_col(s_, idx) for s_ in ("VS1", "VS4", "VS5", "VS7", "VS9")] + \
                 [db, sqltype, code, area, origin, rule]
        for c, v in enumerate(values, 1):
            ws.cell(r, c, v)
        ws.cell(r, 3).font = Font(name=FONT, bold=True)
        fill = NEW if excel.startswith("(not in") else (CALC if origin != "Capture" else None)
        if fill:
            for c in range(1, len(headers) + 1):
                ws.cell(r, c).fill = PatternFill("solid", fgColor=fill)
        r += 1
    body(ws, hr + 1, r - 1, len(headers))
    for c in range(5, 10):
        for rr in range(hr + 1, r):
            ws.cell(rr, c).alignment = Alignment(horizontal="center", vertical="top")
    widths = [5, 20, 30, 34, 8, 8, 8, 8, 8, 34, 32, 26, 20, 13, 60]
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(i)].width = w
    ws.freeze_panes = ws.cell(hr + 1, 4)
    ws.auto_filter.ref = f"A{hr}:{get_column_letter(len(headers))}{r - 1}"
    leg = r + 1
    ws.cell(leg, 2, "Legend").font = Font(name=FONT, bold=True)
    for k, (color, text) in enumerate([
        (None, "No color: captured by an area"),
        (CALC, "Grey: calculated by the system (not captured)"),
        (NEW, "Light orange: calculated by the application, did not exist in the Excel"),
    ]):
        cell = ws.cell(leg + 1 + k, 2, text)
        cell.font = Font(name=FONT, size=10)
        if color:
            cell.fill = PatternFill("solid", fgColor=color)
    ws.cell(leg + 5, 2, "The Excel 'LOW export' column held two values: it is split into LOW Export Number and LOW Export Date. "
                        "Columns per sheet: VS4 = VS1; VS5 has an extra empty column ('.') after PO; VS7 is shifted one column to the right; VS9 starts at column A.").font = Font(name=FONT, size=10, italic=True)

    # ---- Sheet 2 · Status category ----
    ws2 = wb.create_sheet("Status Category")
    title(ws2, "Status category (Excel COMMENTS column)",
          "Exact text of each option and the VS sheets whose drop-down offers it. Oracle: IB_PROCESS_STATUS_CATEGORIES and IB_PROCESS_STATUS_CAT_VS.")
    h2 = ["Option (Excel text)", "Kind", "Responsible area", "Affects duty", "Automatic", "VS1", "VS4", "VS5", "VS7", "VS9"]
    for i, h in enumerate(h2, 1):
        ws2.cell(4, i, h)
    style_header(ws2, 4, len(h2))
    for k, (text, kind, area, duty, auto) in enumerate(COMMENTS):
        row = 5 + k
        vals = [text, kind, area, duty, auto] + ["Yes" if text in COMMENTS_BY_VS[v] else "—" for v in ("VS1", "VS4", "VS5", "VS7", "VS9")]
        for c, v in enumerate(vals, 1):
            ws2.cell(row, c, v)
    last = 4 + len(COMMENTS)
    body(ws2, 5, last, len(h2))
    for rr in range(5, last + 1):
        for c in range(2, len(h2) + 1):
            ws2.cell(rr, c).alignment = Alignment(horizontal="center", vertical="top")
    ws2.cell(last + 1, 1, "Options per sheet").font = Font(name=FONT, bold=True, size=10)
    for c in range(6, 11):
        col = get_column_letter(c)
        cell = ws2.cell(last + 1, c, f'=COUNTIF({col}5:{col}{last},"Yes")')
        cell.font = Font(name=FONT, bold=True, size=10)
        cell.alignment = Alignment(horizontal="center")
    for i, w in enumerate([44, 14, 18, 16, 14, 8, 8, 8, 8, 8], 1):
        ws2.column_dimensions[get_column_letter(i)].width = w
    ws2.freeze_panes = "B5"

    # ---- Sheet 3 · Oracle tables ----
    ws3 = wb.create_sheet("Oracle Tables")
    title(ws3, "Oracle tables (prefix IB_PROCESS_)", "Scripts: database/oracle/01_create_tables.sql, 02_seed_data.sql, 03_comments_and_triggers.sql")
    h3 = ["Table", "What it stores", "Equivalent in the Excel 2.0"]
    for i, h in enumerate(h3, 1):
        ws3.cell(4, i, h)
    style_header(ws3, 4, len(h3))
    for k, row in enumerate(TABLES):
        for c, v in enumerate(row, 1):
            ws3.cell(5 + k, c, v)
        ws3.cell(5 + k, 1).font = Font(name=FONT, bold=True)
    body(ws3, 5, 4 + len(TABLES), len(h3))
    for i, w in enumerate([40, 80, 46], 1):
        ws3.column_dimensions[get_column_letter(i)].width = w
    note = 6 + len(TABLES)
    ws3.cell(note, 1, "The BALANCE sheet and the side blocks (AFTER JUNE 2025, FINISH GOOD RETURN, MISSING) are not tables: "
                      "they are sums of IB_PROCESS_PALLET_RETURNS by VS and period (Balance / Duties screen).").font = Font(name=FONT, size=10, italic=True)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    wb.save(OUT)
    print("OK ->", os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
