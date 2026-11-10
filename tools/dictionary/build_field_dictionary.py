#!/usr/bin/env python3
"""
Genera entregables/Diccionario_Campos_ExpoReturn.xlsx: diccionario entre el nombre de cada campo
en la aplicación, su columna en la base de datos y el encabezado/columna que tenía en el Excel
MATRIZ DE RASTREO 2.0 (hojas VS1, VS4, VS5, VS7 y VS9).

    python3 tools/dictionary/build_field_dictionary.py

Si cambia un nombre en la app (src/ExpoReturn.Desktop/FieldHelp.cs) o en la BD (database/01_schema.sql),
actualice la tabla FIELDS de este script y vuelva a generar el archivo.
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

# (etapa, nombre en la app, campo en el código, columna BD, tipo SQL, encabezado en el Excel,
#  índice de columna en VS1 (None = no está en la hoja), área que lo captura, origen, regla, variantes por hoja)
FIELDS = [
    ("Identidad", "Value Stream (VS)", "ValueStreamId", "ValueStreamId", "TINYINT → cat.Catalog_ValueStreams",
     "(nombre de la hoja)", None, "Al crear el retorno", "Captura",
     "Define el consecutivo del folio y quién puede editar.", "Cada hoja VS1/VS4/VS5/VS7/VS9 es un Value Stream"),
    ("Identidad", "Folio", "FolioNumber", "FolioNumber (+ FolioCode calculado)", "INT", "#", 2, "Sistema", "Sistema",
     "Consecutivo por VS que asigna el sistema al guardar; se muestra como VS7-00012.", ""),
    ("1 · Solicitud", "Responsable QMM (Champion / Proxy)", "QmmResponsible", "QmmResponsibleUserId", "INT → sec.Users",
     "QMM Responsible", 3, "QMM", "Captura", "Champion o Proxy QMM del VS según la matriz Champions VS. Obligatorio.", ""),
    ("1 · Solicitud", "Departamento solicitante", "RequestingDepartment", "RequestingDepartmentId", "SMALLINT → cat.Catalog_Departments",
     "Requesting department", 4, "QMM / área solicitante", "Captura", "Departamento que pide el retorno. Obligatorio.", ""),
    ("1 · Solicitud", "Solicitante (Apellido, Nombre)", "RequestorName", "RequestorName", "NVARCHAR(150)",
     "Requestor", 5, "QMM / área solicitante", "Captura", "Persona que solicita. Obligatorio.", ""),
    ("1 · Solicitud", "Motivo oficial", "ReasonId", "ReasonId", "SMALLINT → cat.Catalog_Reasons",
     "Reason", 6, "QMM / área solicitante", "Captura", "Uno de los 10 motivos de la lista del Excel. Obligatorio.", ""),
    ("1 · Solicitud", "Detalle del motivo", "ReturnReasonDetail", "ReturnReasonDetail", "NVARCHAR(500)",
     '"Return Reason" 22.09.26', 7, "QMM / área solicitante", "Captura", "Texto libre que explica el caso.", ""),
    ("1 · Solicitud", "Número de parte devuelto (13 dígitos)", "ReturnedMaterial", "ReturnedMaterial", "VARCHAR(18)",
     "Returned Pallet 13 Digits Part number", 8, "QMM / área solicitante", "Captura",
     "Formato SAP XXXX.XXX.XXX-XX(X); se normaliza si se captura sin puntos. Obligatorio.", ""),
    ("1 · Solicitud", "Costo unitario MM60 (USD)", "UnitCostUsd", "UnitCostUSD", "DECIMAL(18,6)",
     "MM60 SAP UNIT COST", 9, "Sistema", "Calculado", "Se toma del maestro MM60 (Price / Price unit) al capturar el número de parte.", ""),
    ("1 · Solicitud", "Valor total SAP (USD)", "SapUsdTotal", "SapUsdTotal", "Columna calculada",
     "SAP USD TOTAL", 10, "Sistema", "Calculado", "Cantidad importada × costo unitario.", ""),
    ("1 · Solicitud", "Cantidad importada", "QtyImported", "QtyImported", "INT",
     "Quantity imported", 11, "QMM / área solicitante", "Captura", "Piezas importadas temporalmente; entero mayor a 0. Obligatorio.",
     "VS1 y VS4: 'Quantity'"),
    ("1 · Solicitud", "HU original (10 dígitos)", "OriginalHU", "OriginalHU", "VARCHAR(20)",
     "Original #HU", 12, "QMM / área solicitante", "Captura", "HU de 10 dígitos. Se captura el HU o el lote.", ""),
    ("1 · Solicitud", "Lote original", "OriginalBatch", "OriginalBatch", "VARCHAR(10)",
     "Original Batch", 13, "QMM / área solicitante", "Captura", "Lote SAP: dos letras y ocho dígitos.", ""),
    ("1 · Solicitud", "Fecha PO / retorno a almacén", "DateReturnedToWhs", "DateReturnedToWhs", "DATE",
     "Date PO", 14, "QMM", "Captura", "Fecha de la PO / retorno al almacén de El Paso. [VALIDAR significado]",
     "VS4: 'Date Returned to WHS'"),
    ("1 · Solicitud", "PO de retorno", "ReturnPONumber", "ReturnPONumber", "VARCHAR(20)",
     "PO Number", 18, "QMM", "Captura", "8 dígitos; puede llevar la posición como en el Excel (49398082 - 999).",
     "VS5, VS7 y VS9: 'Return PO'"),
    ("2 · Aduana / entrada", "Fecha de importación", "ImportDate", "ImportDate", "DATE",
     "Import Date", 15, "GS/OSD43", "Captura", "No anterior a la fecha PO.", ""),
    ("2 · Aduana / entrada", "Factura de importación", "ImportInvoice", "ImportInvoice", "VARCHAR(20)",
     "Invoice", 16, "GS/OSD43", "Captura", "MPS + 8 dígitos; su año debe coincidir con la fecha de importación.", ""),
    ("2 · Aduana / entrada", "Fecha de cruce El Paso → JuP1", "ImportDateElpJup1", "ImportDateElpJup1", "DATE",
     "Import date ELP-JuP1", 17, "GS/OSD43", "Captura",
     "Abre el ciclo IMMEX (antigüedad y plazo legal). Requiere fecha de importación y factura.",
     "En el Excel suele traer fecha + factura en el mismo texto"),
    ("2 · Aduana / entrada", "Número de entrega (LOP)", "DeliveryNumber", "DeliveryNumber", "VARCHAR(20)",
     "Delivery Number 22.09.26", 19, "LOP", "Captura", "Número de entrega que registra LOP.", ""),
    ("3 · Ejecución MFO", "Número de parte nuevo", "NewMaterial", "NewMaterial", "VARCHAR(18)",
     "Returned Pallet 13 Dgt New PN", 20, "MFO", "Captura", "Obligatorio y distinto al original si el motivo es reflash.", ""),
    ("3 · Ejecución MFO", "Entregado físicamente a LOW", "PhysicalDeliveredQty", "PhysicalDeliveredQty", "INT",
     "Physical material delivered to LOW", 21, "MFO", "Captura",
     "Igual a material original devuelto + reinsertado. Al capturarlo se cierra el retrabajo MFO.", ""),
    ("3 · Ejecución MFO", "HU nuevo", "NewHU", "NewHU", "VARCHAR(20)",
     "New HU", 22, "MFO", "Captura", "HU de 10 dígitos.", ""),
    ("3 · Ejecución MFO", "Lote del retrabajo", "ReworkBatch", "ReworkBatch", "VARCHAR(10)",
     "Original Batch (segunda columna)", 23, "MFO", "Captura", "Lote del pallet retrabajado.",
     "El Excel repite el encabezado 'Original Batch'"),
    ("3 · Ejecución MFO", "Material reinsertado (cantidad)", "ReinsertionQty", "ReinsertionQty", "INT",
     "New Reinsertion Material Qty", 24, "MFO", "Captura", "Material NUEVO agregado al pallet (no es importación temporal).", ""),
    ("3 · Ejecución MFO", "Fecha de regreso", "DateSentBack", "DateSentBack", "DATE",
     "Date send Back", 25, "MFO", "Captura", "No anterior al cruce.", ""),
    ("3 · Ejecución MFO", "Material original devuelto a LOW", "OldMaterialDeliveredQty", "OldMaterialDeliveredQty", "INT",
     "Old Material Delivered to LOW", 26, "MFO", "Captura", "Piezas IMPORTADAS que regresan a LOW; devuelto + scrap ≤ importado.", ""),
    ("3 · Ejecución MFO", "Cantidad a scrap (HSE)", "ScrapQty", "ScrapQty", "INT",
     "Scrap to HSE (Qty)", 27, "MFO", "Captura", "Piezas importadas que se destruyen; si es mayor a 0 requiere fecha de scrap.", ""),
    ("3 · Ejecución MFO", "Fecha de scrap", "ScrapDate", "ScrapDate", "DATE",
     "Scrap Date", 28, "MFO", "Captura", "No anterior al cruce.", ""),
    ("4 · Cierre salida / HSE", "Expo LOW", "LowExport", "LowExport", "NVARCHAR(200)",
     "LOW export", 29, "LOW", "Captura", "Texto de LOW con el número y la fecha de la expo.", ""),
    ("4 · Cierre salida / HSE", "Exportación aduanal (PRyy-##### · F4 · V1)", "CustomsExportReference", "CustomsExportReference", "VARCHAR(30)",
     "Customs Export", 30, "GS/OSD43", "Captura", "Sólo PRyy-#####, F4 o V1 cierran el pallet; textos o fórmulas no.", ""),
    ("4 · Cierre salida / HSE", "Número de GR (MFO)", "ScrapGrNumber", "ScrapGrNumber", "VARCHAR(10)",
     "GR Number", 31, "MFO", "Captura", "Número de GR del scrap, 10 dígitos.", ""),
    ("4 · Cierre salida / HSE", "Kg destruidos (HSE)", "HseScrapKg", "HseScrapKg", "DECIMAL(10,3)",
     "Qty (Kg). from HSE", 32, "HSE", "Captura", "Kilos destruidos, mayor a 0.", "VS4, VS5, VS7 y VS9: 'Kg Qty from HSE'"),
    ("4 · Cierre salida / HSE", "Fecha de confirmación HSE", "HseConfirmationDate", "HseConfirmationDate", "DATE",
     "HSE confirmation Date", 33, "HSE", "Captura", "Con GR + kg + fecha el scrap sale del balance.", ""),
    ("5 · Balance y estatus", "Balance general (USD)", "BalanceGeneralUsd", "BalanceGeneralUSD", "DECIMAL(18,2)",
     "Balance GENERAL DLLS", 34, "Sistema", "Calculado", "Inventario MFO + scrap HSE sin confirmar + falla MFO-LOW.", ""),
    ("5 · Balance y estatus", "Inventario MFO (USD)", "MfoInventoryUsd", "MfoInventoryUSD", "DECIMAL(18,2)",
     "MFO INVENTORY USD", 35, "Sistema", "Calculado", "Piezas aún en piso MFO × costo.", ""),
    ("5 · Balance y estatus", "Scrap HSE sin confirmar (USD)", "HseScrapUsd", "HseScrapUSD", "DECIMAL(18,2)",
     "HSE SCRAP USD", 36, "Sistema", "Calculado", "Scrap sin confirmación HSE × costo.", ""),
    ("5 · Balance y estatus", "Falla MFO-LOW (USD)", "MfoLowFailureUsd", "MfoLowFailureUSD", "DECIMAL(18,2)",
     "MFO-LOW FAILURE USD", 37, "Sistema", "Calculado", "(En LOW sin pedimento + faltantes) × costo.", ""),
    ("5 · Balance y estatus", "Aranceles por pagar (USD)", "DutiesToPayUsd", "DutiesToPayUSD", "DECIMAL(18,2)",
     "DUTIES TO PAY USD", "DUTIES", "Sistema", "Calculado", "Falla MFO-LOW × tasa (0.19 provisional).",
     "Bloque lateral 'AFTER JUNE 2025' (no existe en VS1)"),
    ("5 · Balance y estatus", "Categoría de estatus", "StatusCategoryCode", "StatusCategoryId", "SMALLINT → cat.Catalog_StatusCategories",
     "COMMENTS", 38, "QMM", "Captura",
     "Opciones de la lista de la hoja VS (ver hoja 'Categoría de estatus'). EXPORT, SCRAP e INVENTORY se asignan solas.",
     "VS4, VS7 y VS9: 'Date - Name:'; VS5: 'Column2'"),
    ("Calculado por la app", "Estatus", "Status", "WorkflowStatusId", "TINYINT → cat.Catalog_WorkflowStatus",
     "(no existe en el Excel)", None, "Sistema", "Calculado", "Borrador, Solicitado, Importado, En retrabajo, Pendiente HSE, Pendiente exportación, Riesgo arancelario, Exportado, Scrap confirmado, Cerrado c/aranceles pagados.", ""),
    ("Calculado por la app", "Riesgo arancelario / motivo", "IsDutyRisk / DutyRiskReason", "IsDutyRisk, DutyRiskReason", "BIT, NVARCHAR(200)",
     "(no existe en el Excel)", None, "Sistema", "Calculado", "Plazo IMMEX por vencer o vencido, faltante o discrepancia.", ""),
    ("Calculado por la app", "Vence plazo IMMEX", "LegalDueDate", "LegalDueDate", "DATE",
     "(no existe en el Excel)", None, "Sistema", "Calculado", "Fecha de cruce + plazo IMMEX (540 días provisional, [VALIDAR GS/OSD43]).", ""),
]

# Ubicación del bloque 'DUTIES TO PAY USD' (AFTER JUNE 2025) en cada hoja
DUTIES_COL = {"VS1": None, "VS4": "AW", "VS5": "AY", "VS7": "AX", "VS9": "AT"}

COMMENTS = [  # texto exacto, tipo, área responsable, afecta aranceles, automática
    ("EXPORT", "Resultado", "—", "No", "Sí"),
    ("SCRAP", "Resultado", "—", "No", "Sí"),
    ("INVENTORY", "Resultado", "—", "No", "Sí"),
    ("ENVIO NACIONAL", "Resultado", "GS/OSD43", "No", "No"),
    ("SCRAP SIN SALIDA FISICA", "Discrepancia", "HSE", "Sí", "No"),
    ("NO REPORTADO CORRECTAMENTE", "Discrepancia", "—", "Sí", "No"),
    ("NO REPORTADO CON CONO VERDE", "Discrepancia", "MFO", "Sí", "No"),
    ("NO LLEGO CON CONO", "Discrepancia", "LOP", "Sí", "No"),
    ("MATERIAL CON CONO NO REPORTADO POR LOW", "Discrepancia", "LOW", "Sí", "No"),
    ("MATERIAL NO LLEGO DE ELP", "Discrepancia", "LOP", "Sí", "No"),
    ("LOW-NO REPORTADO (PLATAFORMA,CONO,RMA)", "Discrepancia", "LOW", "Sí", "No"),
    ("MFO-NO REPORTADO (PLATAFORMA,CONO,RMA)", "Discrepancia", "MFO", "Sí", "No"),
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
    ("trk.Pallets_Return_Tracking", "Un renglón por pallet retornado: todos los campos de la hoja 'Diccionario'.", "Un renglón de una hoja VS"),
    ("trk.Folio_Counters", "Último folio por Value Stream.", "Columna #"),
    ("trk.Status_History", "Cambios de estatus calculado (tiempo en cada etapa).", "No existe"),
    ("trk.Alerts", "Alertas (plazo IMMEX, faltantes, plazos internos) y su atención con comentario.", "No existe"),
    ("cat.Catalog_ValueStreams", "VS1, VS4, VS5, VS7, VS9.", "Las 5 hojas VS"),
    ("cat.Catalog_Departments", "Departamentos solicitantes.", "Requesting department"),
    ("cat.Catalog_Reasons", "Los 10 motivos (texto del Excel).", "Lista de 'Reason'"),
    ("cat.Catalog_StatusCategories", "Opciones de la categoría de estatus con su texto exacto, tipo y área responsable.", "Listas de COMMENTS"),
    ("cat.StatusCategory_ValueStreams", "Qué opciones ofrece cada VS y en qué orden.", "Lista desplegable de COMMENTS de cada hoja"),
    ("cat.Catalog_WorkflowStatus", "Los 10 estatus calculados.", "No existe"),
    ("cat.Material_Cost_MM60", "Maestro de costos: material, descripción, precio, unidad de precio, moneda.", "Hoja oculta 'MM60 COST SAP'"),
    ("sec.Users", "Usuarios (Windows / Active Directory).", "Nombres escritos a mano"),
    ("sec.App_Roles", "QMM, MFQ, GS, LOP, MFO, LOW, HSE, Admin, Consulta.", "Fila 'Areas responsible to fill out information'"),
    ("sec.Champions_Matrix", "Persona × área × VS × Champion/Proxy; alta y baja con fecha registrada por el sistema.", "Hoja 'Champions VS'"),
    ("sec.Field_Permissions", "Qué campos edita cada área.", "Fila de áreas sobre los encabezados"),
    ("aud.Audit_Logs", "Bitácora: quién cambió qué campo, cuándo, valor anterior y nuevo (inmodificable).", "No existe"),
    ("cfg.Parameters", "Tasa de aranceles (0.19), plazo IMMEX (540 días) y umbrales de alertas; ambos valores por validar con GS/OSD43.", "Números fijos dentro de las fórmulas"),
    ("cfg.Fiscal_Periods / cfg.Reporting_Windows", "Periodos fiscales y ventanas de reporte (AFTER JUNE 2025, + NOV 2025…).", "Fechas fijas en las fórmulas de los bloques laterales"),
    ("stg.* (04_staging_migration.sql)", "Carga del Excel 2.0 histórico y observaciones de calidad de datos.", "Importación del archivo"),
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

    # ---- Hoja 1 · Diccionario ----
    ws = wb.active
    ws.title = "Diccionario"
    title(ws, "Expo & Return FG Follow Up · Diccionario de campos",
          f"Nombre en la aplicación ↔ columna en la base de datos ↔ encabezado en el Excel MATRIZ DE RASTREO 2.0 · generado {dt.date.today():%d/%m/%Y}")
    headers = ["#", "Etapa / pestaña", "Nombre en la aplicación", "Encabezado en el Excel 2.0",
               "Col. VS1", "Col. VS4", "Col. VS5", "Col. VS7", "Col. VS9", "Variantes del encabezado por hoja",
               "Columna en la BD (trk.Pallets_Return_Tracking)", "Tipo SQL", "Campo en el código", "Área que lo captura",
               "Captura / cálculo", "Regla o descripción"]
    hr = 4
    for i, h in enumerate(headers, 1):
        ws.cell(hr, i, h)
    style_header(ws, hr, len(headers))
    r = hr + 1
    for n, (stage, label, code, db, sqltype, excel, idx, area, origin, rule, variants) in enumerate(FIELDS, 1):
        values = [n, stage, label, excel] + [sheet_col(s, idx) for s in ("VS1", "VS4", "VS5", "VS7", "VS9")] + \
                 [variants, db, sqltype, code, area, origin, rule]
        for c, v in enumerate(values, 1):
            ws.cell(r, c, v)
        ws.cell(r, 3).font = Font(name=FONT, bold=True)
        fill = NEW if excel.startswith("(no existe") else (CALC if origin != "Captura" else None)
        if fill:
            for c in range(1, len(headers) + 1):
                ws.cell(r, c).fill = PatternFill("solid", fgColor=fill)
        r += 1
    body(ws, hr + 1, r - 1, len(headers))
    for c in range(5, 10):
        for rr in range(hr + 1, r):
            ws.cell(rr, c).alignment = Alignment(horizontal="center", vertical="top")
    widths = [5, 20, 34, 34, 8, 8, 8, 8, 8, 30, 34, 30, 24, 22, 13, 60]
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(i)].width = w
    ws.freeze_panes = ws.cell(hr + 1, 4)
    ws.auto_filter.ref = f"A{hr}:{get_column_letter(len(headers))}{r - 1}"
    leg = r + 1
    ws.cell(leg, 2, "Leyenda").font = Font(name=FONT, bold=True)
    for k, (color, text) in enumerate([
        (None, "Sin color: campo que captura un área"),
        (CALC, "Gris: lo calcula el sistema (no se captura)"),
        (NEW, "Naranja claro: dato calculado por la aplicación que no existía en el Excel"),
    ]):
        cell = ws.cell(leg + 1 + k, 2, text)
        cell.font = Font(name=FONT, size=10)
        if color:
            cell.fill = PatternFill("solid", fgColor=color)
    ws.cell(leg + 5, 2, "Columnas por hoja: VS4 igual que VS1; VS5 tiene una columna vacía extra ('.') después de PO; "
                        "VS7 está recorrida una columna a la derecha (A oculta con 'Pallet_1'); VS9 empieza en la columna A.").font = Font(name=FONT, size=10, italic=True)

    # ---- Hoja 2 · Categoría de estatus ----
    ws2 = wb.create_sheet("Categoría de estatus")
    title(ws2, "Categoría de estatus (columna COMMENTS del Excel)",
          "Texto exacto de cada opción y en qué hoja VS aparece en la lista desplegable. BD: cat.Catalog_StatusCategories y cat.StatusCategory_ValueStreams.")
    h2 = ["Opción (texto del Excel)", "Tipo", "Área responsable", "Afecta aranceles", "Se asigna sola", "VS1", "VS4", "VS5", "VS7", "VS9"]
    for i, h in enumerate(h2, 1):
        ws2.cell(4, i, h)
    style_header(ws2, 4, len(h2))
    for k, (text, kind, area, duty, auto) in enumerate(COMMENTS):
        row = 5 + k
        vals = [text, kind, area, duty, auto] + ["Sí" if text in COMMENTS_BY_VS[v] else "—" for v in ("VS1", "VS4", "VS5", "VS7", "VS9")]
        for c, v in enumerate(vals, 1):
            ws2.cell(row, c, v)
    last = 4 + len(COMMENTS)
    body(ws2, 5, last, len(h2))
    for rr in range(5, last + 1):
        for c in range(2, len(h2) + 1):
            ws2.cell(rr, c).alignment = Alignment(horizontal="center", vertical="top")
    ws2.cell(last + 1, 1, "Total de opciones por hoja").font = Font(name=FONT, bold=True, size=10)
    for c in range(6, 11):
        col = get_column_letter(c)
        cell = ws2.cell(last + 1, c, f'=COUNTIF({col}5:{col}{last},"Sí")')
        cell.font = Font(name=FONT, bold=True, size=10)
        cell.alignment = Alignment(horizontal="center")
    for i, w in enumerate([44, 14, 18, 16, 14, 8, 8, 8, 8, 8], 1):
        ws2.column_dimensions[get_column_letter(i)].width = w
    ws2.freeze_panes = "B5"

    # ---- Hoja 3 · Tablas de la BD ----
    ws3 = wb.create_sheet("Tablas de la BD")
    title(ws3, "Tablas de la base de datos (SQL Server)", "Scripts en database/01_schema.sql a 04_staging_migration.sql")
    h3 = ["Tabla", "Qué guarda", "Equivale en el Excel 2.0 a…"]
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
    ws3.cell(note, 1, "La hoja BALANCE y los bloques laterales (AFTER JUNE 2025, FINISH GOOD RETURN, MISSING) no son tablas: "
                      "son sumas de trk.Pallets_Return_Tracking por VS y periodo (pantalla Balance / Aranceles).").font = Font(name=FONT, size=10, italic=True)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    wb.save(OUT)
    print("OK ->", os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
