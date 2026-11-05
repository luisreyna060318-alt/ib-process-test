<!-- GENERADO desde entregables/SRS_ExpoReturn_FG_JuP1_v0.2.docx el 2026-11-05 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# 00 · Diagnóstico AS-IS de la MATRIZ DE RASTREO 2.0


> **Proyecto:** Expo & Return FG Follow Up · JuP1 (planta SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)
>
> **Fuente analizada:** `PRODUCTOS_TERMINADOS_IMPORTADOS_TEMPORALMENTE_MATRIZ_DE_RASTREO_2.0.xlsx`, archivo **real** en uso (5 hojas VS, BALANCE, Champions VS y maestro MM60). Corte del análisis: 05-nov-2026.
>
> **Método:** lectura automatizada de las tablas estructuradas y de los valores cacheados de cada fórmula. El importador del borrador .NET repite el análisis (`LegacyExcelImporter`).

## 1. Volumen y estructura

| **Hoja**  | **Tabla** | **Registros** | **Con cruce ELP-JuP1** | **SAP USD importado (1)** | **Balance que muestra el Excel**       | **Celdas de balance con error** |
|-----------|-----------|---------------|------------------------|---------------------------|----------------------------------------|---------------------------------|
| VS1       | `VS_1`    | 82            | 77                     | 626,286                   | 344,720.60                             | 0                               |
| VS4       | `VS_4`    | 149           | 83                     | 621,962                   | 537,734.44                             | 15 (`#REF!`, `#VALUE!`)         |
| VS5       | `VS_5`    | 98            | 91                     | 1,380,640                 | 223,885.74                             | 0                               |
| VS7       | `VS_7`    | 194           | 183                    | 3,109,689                 | **538,598.04** (son las cifras de VS4) | 59 (`#VALUE!`, `#REF!`)         |
| VS9       | `VS_9`    | 102           | 101                    | 1,662,239                 | 840,264.46                             | 0                               |
| **Total** |           | **625**       | **535**                | **≈ 7.40 M**              | **2,485,203.28**                       | **74**                          |

\(1\) Suma de `SAP USD TOTAL` (cantidad × costo MM60) de los registros con cruce capturado.

Otras hojas:

- **BALANCE**: resume *Duties to Pay* por VS para los periodos OCT 24–MAY 25 y NOV 25–FEB 26. **5 de sus 10 celdas tienen error** (`#REF!`, `#DIV/0!`, `#VALUE!`). Sólo VS4 (USD 74,938.67) y VS9 (USD 126,150.66) muestran un valor del primer periodo, y VS5 aparece en 0.

- **Champions VS**: Process Owner Ezequiel Loya Morales. Columnas MFO, MFQ (vacía), QMM, LOW y OSD43. **Sólo VS1 tiene LOW y OSD43 asignados**, no existen columnas para LOP ni HSE, y hay un Proxy en "TBD".

- **MM60 COST SAP** (oculta): 33,416 materiales; 429 con precio 0.

## 2. Fórmulas actuales (línea base CalcVersion 1)

Son columnas calculadas idénticas en las 5 tablas, salvo los defectos señalados en §3:

| **Columna**                                        | **Fórmula Excel (simplificada)**                                                                           |
|----------------------------------------------------|------------------------------------------------------------------------------------------------------------|
| MM60 SAP UNIT COST                                 | `XLOOKUP(PN, Table3[Material], Table3[Unit Cost MM60], "COSTEAR", 0)` con `Unit Cost = Price / Price unit` |
| SAP USD TOTAL                                      | `Quantity * UnitCost`                                                                                      |
| Old Material Delivered to LOW                      | `Physical − New Reinsertion` (VS1, VS4, VS9) · `= Physical` (VS7)                                          |
| Customs Export *(fórmula por defecto de la tabla)* | `IF(Quantity − Physical < 0, 0, Quantity − Physical)`                                                      |
| MFO INVENTORY USD                                  | `IF(ISBLANK(ImportDateELP), 0, IF(ISBLANK(Physical), Qty − Old − Scrap, "0")) * Cost`                      |
| HSE SCRAP USD                                      | `IF(ISBLANK(CustomsExport), Scrap, "0") * Cost`                                                            |
| MFO-LOW FAILURE USD                                | `IF(ISBLANK(CustomsExport), Old, "0") * Cost`                                                              |
| Balance GENERAL DLLS                               | `MFO INVENTORY + HSE SCRAP + MFO-LOW FAILURE`                                                              |
| DUTIES TO PAY (resúmenes)                          | `MFO-LOW FAILURE × 0.19` con fechas de corte fijas (`> 31-may-2025`, `> 30-oct-2025`)                      |

## 3. Hallazgos críticos

| **\#** | **Hallazgo**                                                                                                                                             | **Evidencia en el archivo real**                                                                                                                                                                | **Impacto**                                                                                                                                        |
|--------|----------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| H-01   | **Las fórmulas financieras de VS7 leen la tabla** `VS_4`**.**                                                                                            | Las sumas de VS7 son idénticas a las de VS4 (MFO Inventory 47,588.93; MFO-LOW Failure 488,069.58). Hay 59 errores en los renglones donde VS4 ya no tiene datos.                                 | Recalculado con sus propios datos, el balance de VS7 es ≈ **USD 1.24 M**, no 538.6 k: **≈ USD 700 k de exposición no visible**.                    |
| H-02   | `Customs Export` **trae una fórmula por defecto.** Una celda con fórmula no está vacía para Excel, así que el pallet se toma como exportado.             | **57 registros de VS4** tienen la fórmula y ninguna referencia de pedimento.                                                                                                                    | Falsos cierres: HSE Scrap y MFO-LOW Failure quedan en 0 aunque no exista exportación.                                                              |
| H-03   | **El faltante desaparece del balance** al capturar "Physical material delivered to LOW".                                                                 | **54 registros** con `Old + Scrap < Quantity` después de cerrar MFO; 11 con `Old + Scrap > Quantity`.                                                                                           | Piezas sin conciliar que ningún indicador muestra; sobre-salidas que nadie detecta.                                                                |
| H-04   | El scrap sólo sale del balance con un pedimento, no con la confirmación de HSE.                                                                          | Hay 3 registros con kilos o fecha de HSE y `Scrap Qty` en 0.                                                                                                                                    | Scrap destruido que sigue abierto, o se "cierra" con un dato que no aplica.                                                                        |
| H-05   | `Customs Export` se usa para varios tipos de cierre en texto libre.                                                                                      | `F4` (21), `V1` (3), `scrap marzo` (3), `sc1`, `scrap`, `PT-2600787`, `F4-2500046`.                                                                                                             | Hay cierres por **cambio de régimen (F4)** y **pedimento virtual (V1)** que el modelo debe distinguir de la exportación (PR). \[VALIDAR GS/OSD43\] |
| H-06   | **Resúmenes y BALANCE con errores**: el bloque "AFTER JUNE 2025" de VS4 está en `#REF!` y el de VS7 en `#VALUE!`. BALANCE tiene 5 de 10 celdas en error. | Celdas AS11:AW11 (VS4), AS10:AX10 (VS7), BALANCE!C3, D3, D4, C6, D6                                                                                                                             | **Hoy no existe una cifra consolidada confiable de Duties to Pay.**                                                                                |
| H-07   | El factor de duty (0.19) y las fechas de corte están escritos dentro de las fórmulas, con criterios distintos por hoja.                                  | VS9!BK12 usa 31-may; el resto del bloque usa 30-oct.                                                                                                                                            | Un mismo KPI se calcula distinto por VS.                                                                                                           |
| H-08   | **El motivo oficial y el departamento solicitante nunca se capturaron.**                                                                                 | `Reason` 0 de 625; `Requesting department` 0 de 625. El departamento va dentro del texto del solicitante en formatos variados: `MFQ4-Lizbeth…`, `LOP: J.Pablo / D. Rios`, `QMM, PUQ: Abraham…`. | No hay análisis por motivo ni por área; el catálogo de 10 motivos no se usa.                                                                       |
| H-09   | **"Original \#HU" guarda casi siempre el lote**, no la unidad de manipulación.                                                                           | 218 registros con formato de lote (`AA99999999`); 50 con HU + fecha (`2107… 9/12/2025`). New HU: 103 con 9 dígitos y ~60 con formato de lote.                                                   | La regla "HU de 10 dígitos" no refleja la práctica: hay que definir qué identificador se exige. \[VALIDAR\]                                        |
| H-10   | Fechas en texto, en formatos mezclados y con errores.                                                                                                    | 527 Import Date en texto (`JAN.20.2025`, `DIC.11.24`, `ENE.01.2025`); 506 cruces con fecha y factura en la misma celda; `Juio/16/2026`, `17/Dic/2025`, `20-Mar.26`.                             | No se puede medir el aging IMMEX desde el Excel.                                                                                                   |
| H-11   | **Comentarios escritos en columnas de fecha** y fechas inválidas.                                                                                        | `Date send Back` = "ECUs for destructive tests, will be scrapped"; `HSE confirmation Date` = "plataforma amarilla"; `Scrap Date` = 0 en 93 registros de VS7.                                    | Dato operativo perdido en la columna equivocada.                                                                                                   |
| H-12   | **Import Date anterior a "Date PO" en 67 registros.**                                                                                                    | VS1, VS4, VS7                                                                                                                                                                                   | "Date PO" no siempre es "fecha de retorno a WHS"; posiblemente es la fecha de la PO de retorno. \[VALIDAR\]                                        |
| H-13   | Factura y PO con formatos inconsistentes.                                                                                                                | Facturas `MP########` (32) y `MPS#######` (25); PO con decimales (`49398082.1`), con posición (`- 999`) o con referencia aduanal (`PR25-…`).                                                    | Impide cruzar con documentos aduanales y de compras.                                                                                               |
| H-14   | QMM Responsible vacío o con variantes de nombre.                                                                                                         | 79 vacíos; la misma persona escrita de 2 o 3 formas (inicial vs. nombre completo, con y sin segundo apellido, con errores de dedo); un valor que no es persona (`TDG1-5.1 CBO - Nissan`).       | No se puede medir carga ni desempeño por responsable.                                                                                              |
| H-15   | La categoría de estatus más usada es genérica.                                                                                                           | `NO REPORTADO CORRECTAMENTE`: 144 registros; las listas de validación son distintas por hoja (de 7 a 12 valores) y en VS5 sólo cubren 2 celdas.                                                 | La causa raíz real no se puede analizar.                                                                                                           |
| H-16   | Costos pendientes y valores no numéricos.                                                                                                                | 10 `COSTEAR` y 1 `.` en el costo; 11 SAP USD TOTAL en `#VALUE!`.                                                                                                                                | Valor en riesgo subestimado.                                                                                                                       |
| H-17   | No hay bitácora de cambios, bloqueo por área, concurrencia controlada ni alertas de vencimiento.                                                         | —                                                                                                                                                                                               | Riesgo aduanal (IMMEX) y financiero sin detección temprana.                                                                                        |

## 4. Impacto cuantificado (preliminar)

| **Concepto**                                                                                        | **USD**       |
|-----------------------------------------------------------------------------------------------------|---------------|
| Balance que hoy muestra el Excel (suma de las 5 hojas, sin errores)                                 | 2,485,203     |
| Balance recalculado con las **mismas** fórmulas, pero cada VS con sus propios datos (CalcVersion 1) | 3,187,935     |
| **Exposición oculta sólo por H-01 (VS7)**                                                           | **≈ 700,000** |
| Balance con las reglas corregidas (CalcVersion 2, importador del borrador)                          | ≈ 3,360,000   |

> Las cifras v2 son preliminares: dependen de depurar las fechas en texto, los cierres F4/V1 y los faltantes con los Champions de cada VS (doc 04 §2.4). Su propósito es dimensionar el riesgo, no sustituir la conciliación formal.

## 5. Casos de prueba "golden"

Se codificaron en `ExpoReturn.Domain.Tests` y pasan con el motor de cálculo del borrador. Detalle en la Sección 03 §6.

| **Caso**    | **Situación**                                                                  | **Balance Excel** | **Balance v2** | **Estatus v2** |
|-------------|--------------------------------------------------------------------------------|-------------------|----------------|----------------|
| G-VS7       | 576 pzs entregadas a LOW, "expo \#4" sin pedimento, Customs Export con fórmula | 0.00              | **11,892.50**  | PENDING_EXPORT |
| G-VS9       | 528 importadas, 527 entregadas, HSE confirmó 0.5 kg con scrap en 0             | 0.00              | **23.59**      | DUTY_RISK      |
| G-VS9-fix   | Igual que el anterior, con Scrap Qty = 1                                       | —                 | 0.00           | EXPORTED       |
| Scrap 100 % | 100 pzs a scrap: pendiente hasta la confirmación de HSE (GR + kg + fecha)      | —                 | 0.00           | SCRAPPED       |
| Aging       | Cruce hace 525 días (plazo provisional 540)                                    | —                 | \> 0           | DUTY_RISK      |
