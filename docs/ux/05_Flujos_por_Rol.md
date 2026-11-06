<!-- GENERADO desde entregables/UX_Manual_Pantallas_ExpoReturn_v0.2.docx el 2026-11-06 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# 05 · Flujos por rol


Recorridos típicos de cada área en la aplicación. Sirven como guion para las revisiones con usuarios y como guía rápida para la capacitación.

## Ciclo de vida del pallet y pantalla donde ocurre

| **Paso**                                | **Quién**             | **Pantalla / pestaña**         | **Estatus resultante**                               |
|-----------------------------------------|-----------------------|--------------------------------|------------------------------------------------------|
| 1\. Abrir la solicitud                  | QMM o solicitante     | PNT-02 · Pestaña 1             | Borrador → Solicitado                                |
| 2\. Registrar la importación y el cruce | GS/OSD43 (+ QMM, LOP) | PNT-02 · Pestaña 2             | Importado                                            |
| 3\. Retrabajar / sortear                | MFO                   | PNT-02 · Pestaña 3             | En retrabajo → Pendiente exportación / Pendiente HSE |
| 4a. Exportar el material bueno          | LOW y GS/OSD43        | PNT-02 · Pestaña 4             | Exportado                                            |
| 4b. Confirmar el scrap                  | MFO (GR) y HSE        | PNT-02 · Pestaña 4             | Scrap confirmado / Exportado                         |
| 5\. Clasificar discrepancias            | QMM                   | PNT-02 · Pestaña 5             | Riesgo arancelario → Cerrado c/aranceles pagados     |
| Seguimiento diario                      | Todas las áreas       | PNT-04 Alertas, PNT-01 Bandeja | —                                                    |
| Reporte                                 | Jefaturas             | PNT-03 Balance / Aranceles     | —                                                    |

## Flujo QMM: nuevo retorno

1.  Menú **Nuevo retorno** (usuario simulado: QMM del VS).

2.  El Value Stream viene sugerido. Elegir **QMM Responsible**, **Requesting department** y **Reason**, y capturar **Requestor**.

3.  Capturar el **número de parte** (puede ser sin puntos) y la **cantidad**. El costo MM60 y el SAP USD Total aparecen solos.

4.  Capturar el HU o el lote original.

5.  Revisar el panel de validaciones: cuando ya no hay avisos de la Etapa 1, el estatus pasa a **Solicitado**.

6.  **Guardar**: se asigna el folio `VSn-#####` y aparece el primer renglón en Auditoría.

## Flujo GS/OSD43: importación y exportación

1.  En la Bandeja, filtrar Estatus = Solicitado y abrir el folio.

2.  Pestaña 2: Import Date, Invoice (MPS########) y **cruce ELP-JuP1** → estatus **Importado**.

3.  Cuando LOW registre la expo, pestaña 4: capturar **Customs Export** `PRyy-#####` y su fecha → estatus **Exportado** si no queda nada pendiente.

## Flujo MFO: retrabajo y cierre

1.  Alertas → "SLA retrabajo MFO", o la Bandeja filtrada por el VS → abrir el folio.

2.  Pestaña 3: tipo y número de orden IB, New HU, Old Material, Scrap y Scrap Date.

3.  Capturar **Physical delivered** para cerrar el retrabajo. Si faltan piezas, la validación VAL-034 lo impide hasta corregir o **Declarar faltante**.

4.  Pestaña 4: GR Number del scrap.

## Flujo LOW y HSE

- **LOW:** pestaña 4, número y fecha de la expo, y nota.

- **HSE:** pestaña 4, kg destruidos y fecha de confirmación. Con el GR que capturó MFO, el scrap sale del balance.

## Flujo jefatura: seguimiento

1.  **Balance / Aranceles**: exposición por VS y por año fiscal.

2.  **Alertas**: críticas primero (plazo IMMEX y faltantes).

3.  **Bandeja** con "Sólo riesgo arancelario" para revisar caso por caso.
