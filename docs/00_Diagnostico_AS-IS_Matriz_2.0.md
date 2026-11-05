# 00 · Diagnóstico AS-IS de la MATRIZ DE RASTREO 2.0

> **Proyecto:** Expo & Return FG Follow Up · JuP1 (planta SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)
> **Fuente analizada:** `PRODUCTOS_TERMINADOS_IMPORTADOS_TEMPORALMENTE_MATRIZ_DE_RASTREO_2.0.xlsx` (versión de muestra, 1 registro por VS)
> **Propósito:** documentar la lógica actual del Excel, que es la línea base que la aplicación debe replicar (CalcVersion 1) y luego corregir (CalcVersion 2).

---

## 1. Estructura del libro

| Hoja | Estado | Tabla estructurada | Rango tabla | Filas en el rango | Registros con datos |
|---|---|---|---|---|---|
| VS1 | visible | `VS_1` | C6:AL89 | 83 | 1 |
| VS4 | visible | `VS_4` | C7:AL148 | 141 | 1 |
| VS5 | visible | `VS_5` | C6:AM103 | 97 | 1 |
| VS7 | visible | `VS_7` | D7:AM201 | 194 | 1 |
| VS9 | visible | `VS_9` | B7:AK110 | 103 | 1 |
| MM60 COST SAP | **oculta** | `Table3` | A1:H33417 | 33,416 materiales | — |

Las hojas **BALANCE_** y **Champions_VS** que menciona la especificación **no vienen en el archivo de muestra**. El diseño las cubre (vistas `rpt.vw_Balance_ByVS_Period` y tabla `sec.Champions_Matrix`), pero hay que conseguir las originales para validar la migración.

Cada hoja VS tiene 36 columnas de captura/cálculo, agrupadas por las áreas responsables (fila de encabezado superior):

```
QMM (C..N) | GS/OSD43 (O..Q) | QMM (R) | LOP (S) | MFO (T..AB) | LOW (AC) | GSOSD (AD) | MFO (AE) | HSE (AF..AG) | Balance (AH..AK) | Comments (AL)
```

A la derecha de cada tabla hay bloques de resumen hechos con `SUMIFS`/`SUMPRODUCT`. Las listas de validación (motivos y estatus) están guardadas en celdas sueltas de la misma hoja.

## 2. Fórmulas actuales (línea base CalcVersion 1)

Son columnas calculadas idénticas en las 5 tablas, salvo los defectos señalados en §3:

| Columna | Fórmula Excel (simplificada) |
|---|---|
| MM60 SAP UNIT COST | `XLOOKUP(PN, Table3[Material], Table3[Unit Cost MM60], "COSTEAR", 0)` donde `Unit Cost MM60 = Price / Price unit` |
| SAP USD TOTAL | `Quantity * UnitCost` |
| Old Material Delivered to LOW | `Physical material delivered to LOW - New Reinsertion Material Qty` (VS1, VS4, VS9) · `= Physical` (VS7) |
| Customs Export *(fórmula por defecto de la tabla)* | `IF(Quantity - Physical < 0, 0, Quantity - Physical)` |
| MFO INVENTORY USD | `IF(ISBLANK(ImportDateELP), 0, IF(ISBLANK(Physical), Qty - Old - Scrap, "0")) * Cost` |
| HSE SCRAP USD | `IF(ISBLANK(CustomsExport), Scrap, "0") * Cost` |
| MFO-LOW FAILURE USD | `IF(ISBLANK(CustomsExport), Old, "0") * Cost` |
| Balance GENERAL DLLS | `MFO INVENTORY + HSE SCRAP + MFO-LOW FAILURE` |

Bloques de resumen por hoja:

| Bloque | Filtro | Indicadores |
|---|---|---|
| "AFTER JUNE 2025" | `Date PO > 31-may-2025` y `Import date ELP-JuP1 <> ""` | TOTAL IMPORTED, MFO INVENTORY, HSE SCRAP, MFO-LOW FAILURE, EXPORTED USD = Total − los 3 anteriores, **DUTIES TO PAY USD = MFO-LOW FAILURE × 0.19** |
| "FINISH GOOD RETURN (QTY) + NOV 2025" | `Date PO > 30-oct-2025` | IMPORT, EXPORTED, PHYSICAL INVENTORY, MISSING = NOT REPORTED SCRAP + MISSING, **DUTY MISSING = (USD de ambos) × 0.19** |
| "FROM OCT 24 TO MAY 25" (VS1) | encabezados por categoría de estatus | NO REPORTADO CORRECTAMENTE, SCRAP, INVENTARIO, EXPORTADO, …, DUTIES TO PAY USD |

## 3. Hallazgos críticos (justifican la migración)

| # | Hallazgo | Evidencia | Impacto |
|---|---|---|---|
| H-01 | **Las columnas financieras de VS7 apuntan a la tabla `VS_4`**: `Balance`, `MFO INVENTORY`, `HSE SCRAP` y `MFO-LOW FAILURE` de `VS_7` usan `VS_4[[#This Row],…]`. | VS7!AI8:AL8 (fórmula de columna calculada de `VS_7`) | Los indicadores de VS7 no reflejan sus propios pallets; el BALANCE de VS7 no es confiable. |
| H-02 | **La columna `Customs Export` trae una fórmula por defecto** (`Qty − Physical`) en las 5 tablas. Una celda con fórmula **nunca es `ISBLANK`**, así que HSE SCRAP y MFO-LOW FAILURE dan 0 aunque no exista pedimento. | VS7!AE8 = 0 (sin referencia PR) y Balance = 0, aunque LOW sólo registró "Incluido en expo #4 de el dia 03/11/2026" | **Falso cierre**: 576 pzs (USD 11,892.50) se ven conciliadas sin exportación aduanal. |
| H-03 | **Faltantes que desaparecen del balance**: en cuanto se captura `Physical material delivered to LOW`, MFO INVENTORY pasa a 0 aunque `Old + Scrap < Quantity`. | VS9 fila 8: importado 528, entregado 527, scrap vacío, HSE confirmó 0.5 kg con GR 4951422891 → Balance = 0 | Hay 1 pieza sin conciliar que nadie ve; el scrap confirmado por HSE no tiene cantidad. |
| H-04 | El scrap sólo sale del balance cuando hay `Customs Export`, no cuando lo confirma HSE. | Fórmula HSE SCRAP USD | Un pallet 100 % scrap con confirmación HSE se queda abierto para siempre (o se "cierra" capturando un pedimento que no aplica). |
| H-05 | Factor de aranceles (`0.19`) y fechas de corte (`DATE(2025,5,31)`, `DATE(2025,10,30)`, `">5/31/2025"`) escritos dentro de las fórmulas. | AU43, AW11, AY9, BF10 … | Cambiar un parámetro implica editar ~40 fórmulas; el criterio de corte ya es distinto entre hojas (VS9!BK12 usa 31-may y el resto del bloque usa 30-oct). |
| H-06 | Resúmenes inconsistentes entre hojas: el `SUMPRODUCT` de NOT REPORTED SCRAP en VS5 no filtra `Customs Export = ""`, mientras que VS1 sí; VS9 añade `GR Number = ""`. | VS1!BH11 vs VS5!BR10 vs VS9!BQ12 | El mismo KPI se calcula distinto según el VS. |
| H-07 | Encabezados distintos para el mismo dato: `Quantity` / `Quantity imported`; `Date PO` / `Date Returned to WHS`; `PO Number` / `Return PO`; `Qty (Kg). from HSE` / `Kg Qty from HSE`; última columna `COMMENTS` / `Date - Name:` / `Column2`; columna extra `.` en VS5; la tabla empieza en la columna B, C o D según la hoja. | Fila de encabezados de cada hoja | Rompe Power Query/Power BI y cualquier consolidado. |
| H-08 | En VS5 están invertidas las etiquetas de área: LOW aparece sobre `Delivery Number` y LOP sobre `LOW export`. | VS5!T5, VS5!AD5 | Confunde quién es responsable de cada dato. |
| H-09 | Fechas guardadas como texto, en formatos mezclados y con errores de captura: `JAN.20.2025`, `MAR.2.2026`, `Juio/16/2026`, `N/A`, `0`. `Import date ELP-JuP1` junta fecha y factura en una sola celda (`JAN.20.2025 MPS26000009`). | VS1!O7, Y7, Q7 · VS5!AC7 · VS7!AC8 | No se pueden calcular días de permanencia (aging IMMEX) ni filtrar por periodo. |
| H-10 | Fechas fuera de orden: Date PO 01-nov-2025 con Import Date "JAN.20.2025" y factura MPS**26**000009 (es muy probable que el año correcto sea 2026). | VS1 fila 7 | Un aging calculado con esa fecha saldría negativo o sin sentido. |
| H-11 | Identificadores mal formados: `Original #HU` con lote + fecha (`AC20887866 12/9/2025`) o sólo el lote (`AA21725146`); New HU de 9 dígitos (`107140724`); PO con posición (`49398082 - 999`); P/N nuevo sin puntos (`0203306644-250`). | VS1!L7, VS9!K8, VS7!W8, VS4!R8, VS4!T8 | Impide cruzar con SAP (HU/PO/material). |
| H-12 | Los campos obligatorios no se llenan: `Requesting department` y `Reason` vienen vacíos en los 5 registros. El departamento aparece metido en el texto del solicitante (`MFO5: Arturo A. Lopez E.`). En VS5 el texto libre dice "Reflasheo…" pero el motivo oficial está vacío. | Columnas D/F de cada hoja | No se puede analizar el retorno por motivo/área en Power BI. |
| H-13 | Las listas de validación están en celdas sueltas y tienen huecos: VS1 `F7:F77 F79:F89` (falta la fila 78), VS7 `G8:G198 G200:G201`, y en VS5 el estatus sólo se valida en `AL98`/`AL99`. Cada hoja tiene una lista de estatus distinta (de 7 a 12 valores). | DataValidations de cada hoja | Captura libre de facto y categorías que no se pueden comparar entre VS. |
| H-14 | El estatus manual contradice los datos: VS1 tiene `COMMENTS = INVENTORY` aunque está exportado (PR26-00095) y su balance es 0. | VS1!AL7 | Los dashboards por categoría no son confiables. |
| H-15 | Maestro MM60 en una hoja oculta de 33,416 filas: 429 materiales con precio 0, 4 filas sin material y el fallback `"COSTEAR"` (texto), que hace que `Qty × Cost` dé `#VALUE!`. | `MM60 COST SAP` | Costos en cero o con error que subvalúan el riesgo; no se sabe a qué fecha corresponde el costo. |
| H-16 | La especificación habla de 9 motivos oficiales, pero la lista del Excel tiene **10** (incluye *Inspect due to mistakes in standard pack*). | VS1!BA40:BA49 | Hay que confirmar el catálogo con QMM; se cargaron los 10. |
| H-17 | No hay bitácora de cambios, bloqueo por área, concurrencia controlada ni alertas de vencimiento. | — | Riesgo aduanal (IMMEX) y financiero sin detección temprana. |

## 4. Casos de prueba "golden" (de los registros de muestra)

Estos 5 registros serán las primeras pruebas unitarias del motor de cálculo (`Domain.Tests`). Detalle completo en [03_Reglas_de_Negocio.md §6](03_Reglas_de_Negocio.md#6-casos-golden-y-pruebas-de-aceptación).

| VS | P/N | Qty | Unit Cost | Balance Excel (v1) | Balance v2 | Estatus v2 | Hallazgo |
|---|---|---|---|---|---|---|---|
| VS1 | 0285.500.708-24W | 60 | 1.2677 | 0.00 | 0.00 | EXPORTED | H-10, H-11, H-14 |
| VS4 | 0203.306.644-250 | 1,440 | 29.9981 | 0.00 | 0.00 | EXPORTED | H-11 |
| VS5 | 0261.S10.8CU-6DY → 0261.S10.8GT-6DV | 168 | 84.3378 | 0.00 | 0.00 | EXPORTED | H-12 (motivo vacío en un reflash) |
| VS7 | F00H.J04.154-24W | 576 | 20.6467 | 0.00 | **11,892.50** | **PENDING_EXPORT** | H-01, H-02 |
| VS9 | 0265.957.012-24W | 528 | 23.5871 | 0.00 | **23.59** | **DUTY_RISK** (faltan 1 pz) | H-03 |
