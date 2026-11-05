# Resumen Ejecutivo

## Contexto

Cuando un pallet de producto terminado de JuP1 se retorna desde El Paso WHS para retrabajo, sorteo o reflasheo (Inhouse Repair / IB Process), entra a México como **importación temporal bajo IMMEX**. Cada pieza importada debe conciliarse al 100 %: con su **re-exportación** a El Paso o con su **destrucción comprobada** por HSE. Si la conciliación falla, la planta paga aranceles e impuestos (*Duties to Pay*) y absorbe la pérdida de inventario (*MFO-LOW Failure*).

Hoy este control vive en un archivo de Excel compartido en SharePoint (**MATRIZ DE RASTREO 2.0**), con una hoja por Value Stream (VS1, VS4, VS5, VS7, VS9), en el que capturan siete áreas: QMM, MFO, MFQ, LOP, LOW, GS/OSD43 y HSE.

## Problema

El análisis del archivo actual (Sección 00) encontró defectos que **ya afectan las cifras que se reportan**:

| Hallazgo | Efecto en el negocio |
|---|---|
| Las fórmulas financieras de la hoja VS7 leen los datos de la tabla de VS4 | El balance de VS7 no corresponde a sus propios pallets |
| La columna *Customs Export* trae una fórmula por defecto; Excel no la considera vacía y el pallet aparece como exportado | **Falsos cierres**: en la muestra, 576 pzs (USD 11,892.50) aparecen conciliadas sin pedimento de exportación |
| Al registrar la entrega a LOW, el faltante desaparece del balance | En la muestra, 1 pieza de VS9 sin conciliar no aparece en ningún indicador |
| Tasa de duty (0.19) y fechas de corte escritas dentro de ~40 fórmulas, con criterios distintos por hoja | Un mismo KPI se calcula distinto en cada VS |
| Fechas en texto ("Juio/16/2026", "JAN.20.2025"), HU con lote y fecha, motivo y departamento vacíos | No se puede medir el aging IMMEX ni conectar Power BI de forma limpia |
| Sin bitácora de cambios, sin control por área y sin alertas de vencimiento | No hay trazabilidad para una auditoría aduanal ni detección temprana del riesgo |

## Propuesta

Reemplazar el Excel por una **aplicación corporativa** con estas características:

* **Base de datos central en SQL Server** de JuP1, con catálogos estrictos, reglas de integridad y vistas listas para los dashboards de Power BI de ME/QMM.
* **Aplicación de escritorio Windows (C# / .NET 8)** en la primera fase. Toda la lógica vive en el servidor (Web API), así que la **migración a Web** en una segunda fase sólo reemplaza la pantalla, sin reescribir reglas ni datos.
* **Flujo en 6 bloques** que respeta las responsabilidades actuales del Excel: cada área edita sólo sus campos y sólo en los VS donde es Champion o Proxy (matriz Champions_VS).
* **Cálculo automático** de los 4 indicadores financieros (Balance General, MFO Inventory, HSE Scrap, MFO-LOW Failure) y del estatus de cada pallet (*Imported, In Rework, Pending HSE, Pending Export, Exported, Duty Risk*), sin captura manual.
* **Alertas automáticas** a las áreas responsables cuando un pallet se acerca al vencimiento de su plazo legal, cuando hay faltantes o cuando se exceden los SLA internos.
* **Auditoría completa**: quién cambió qué dato, cuándo, y su valor anterior y nuevo. Autenticación con el usuario de Windows (Active Directory).
* **Integración futura con SAP P98** (costos MM60, HU, órdenes PPI) por las interfaces aprobadas por Bosch (RFC/BAPI con SSO), sin SAP GUI Scripting.

## Beneficios esperados

| Beneficio | Indicador |
|---|---|
| Visibilidad real del riesgo aduanal y financiero | Exposición USD y Duties to Pay por VS y periodo fiscal, sin falsos cierres |
| Prevención de pagos de duty | Alertas a 60 y 30 días del vencimiento legal; lista diaria de pallets en Duty Risk |
| Datos confiables para Power BI | 0 archivos intermedios; vistas SQL estables |
| Trazabilidad para auditorías | 100 % de los cambios registrados por usuario y campo |
| Edición simultánea sin riesgo | Se acaba la corrupción del archivo y la ruptura de rangos o columnas |
| Base para la Fase Web | Las reglas y los datos se reutilizan al 100 % |

## Plan y tiempos (estimación)

| Fase | Duración estimada | Resultado |
|---|---|---|
| 0 · Gobierno y validación de reglas | 2 semanas | Registro en SOURCE, reglas firmadas por las áreas |
| 1 · Fundaciones técnicas | 4 semanas | Base de datos, API y motor de cálculo probados |
| 2 · MVP de escritorio | 5 semanas | Aplicación con los 6 bloques, permisos, auditoría y alertas |
| 3 · Migración y piloto VS9 | 2 semanas de corrida en paralelo con el Excel (la preparación arranca desde la Fase 1) | Histórico migrado y conciliado al centavo contra el Excel |
| 4 · Go-Live VS1–VS9 | 3 semanas de hypercare | Excel en sólo lectura; la aplicación es la fuente oficial |
| 5 · Integración SAP P98 | 4–6 semanas | Costos y validaciones en línea |
| 6 · Versión Web | 6–8 semanas | Acceso desde el navegador |

Equipo estimado: 1 desarrollador .NET senior, 1 analista de proceso QMM a medio tiempo, y apoyo puntual de DBA/IT (servidor, SPN, Software Center) y de los Champions de cada VS para depurar el histórico. **Go-live estimado: ~4 meses después de aprobar la Fase 0.**

## Decisiones que se solicitan

1. **Aprobar el alcance y la arquitectura** propuestos (escritorio primero, preparada para Web).
2. **Nombrar al dueño del proceso y del dato** (ME/QMM-JuP1 · Process Excellence) para el registro en SOURCE.
3. **Designar responsables** que confirmen los pendientes de negocio de la Sección 04 §8: plazo legal IMMEX, tasa de duty, catálogo de motivos, tipos de orden PPI y valuación del reflash.
4. **Asignar recursos** (desarrollo, DBA/IT) y **confirmar el VS piloto** (se propone VS9).
5. **Aprobar el congelamiento del Excel** (sólo lectura) en la fecha de go-live de cada VS.
