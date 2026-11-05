<!-- GENERADO desde entregables/SRS_ExpoReturn_FG_JuP1_v0.2.docx el 2026-11-05 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# Resumen Ejecutivo


## Contexto

Cuando un pallet de producto terminado de JuP1 se retorna desde El Paso WHS para retrabajo, sorteo o reflasheo (Inhouse Repair / IB Process), entra a México como **importación temporal bajo IMMEX**. Cada pieza importada debe conciliarse al 100 %: con su **re-exportación** a El Paso o con su **destrucción comprobada** por HSE. Si la conciliación falla, la planta paga aranceles e impuestos (*Duties to Pay*) y absorbe la pérdida de inventario (*MFO-LOW Failure*).

Hoy este control vive en un archivo de Excel compartido en SharePoint (**MATRIZ DE RASTREO 2.0**), con una hoja por Value Stream (VS1, VS4, VS5, VS7, VS9), en el que capturan siete áreas: QMM, MFO, MFQ, LOP, LOW, GS/OSD43 y HSE.

## Problema

El análisis del archivo **real** (625 registros, ≈ USD 7.4 M importados temporalmente; Sección 00) encontró defectos que **ya distorsionan las cifras que se reportan**:

| **Hallazgo**                                                                                                                         | **Efecto en el negocio**                                                                                             |
|--------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------|
| Las fórmulas financieras de la hoja VS7 leen los datos de VS4                                                                        | VS7 muestra USD 538.6 k de balance. Con sus propios datos son ≈ USD 1.24 M: **≈ USD 700 k de exposición no visible** |
| La hoja BALANCE y los resúmenes de VS4/VS7 están en error (`#REF!`, `#VALUE!`, `#DIV/0!`)                                            | **No existe hoy una cifra consolidada confiable de Duties to Pay**                                                   |
| 57 registros de VS4 tienen una fórmula en *Customs Export* que los hace parecer exportados                                           | Falsos cierres sin pedimento                                                                                         |
| 54 registros con faltante de piezas que desaparece del balance                                                                       | Riesgo de pago de aranceles que nadie ve                                                                             |
| Motivo y departamento solicitante vacíos en los 625 registros; la columna de HU guarda lotes; fechas en texto con formatos mezclados | No se puede medir el aging IMMEX ni analizar por motivo o área en Power BI                                           |
| Sin bitácora de cambios, sin control por área y sin alertas de vencimiento                                                           | No hay trazabilidad para una auditoría aduanal ni detección temprana del riesgo                                      |

## Propuesta

Reemplazar el Excel por una **aplicación corporativa** con estas características:

- **Base de datos central en SQL Server** de JuP1, con catálogos estrictos, reglas de integridad y vistas listas para los dashboards de Power BI de ME/QMM.

- **Aplicación de escritorio Windows (C# / .NET 8)** en la primera fase. Toda la lógica vive en el servidor (Web API), así que la **migración a Web** en una segunda fase sólo reemplaza la pantalla, sin reescribir reglas ni datos.

- **Flujo en 6 bloques** que respeta las responsabilidades actuales del Excel: cada área edita sólo sus campos y sólo en los VS donde es Champion o Proxy (matriz Champions_VS).

- **Cálculo automático** de los 4 indicadores financieros (Balance General, MFO Inventory, HSE Scrap, MFO-LOW Failure) y del estatus de cada pallet (*Imported, In Rework, Pending HSE, Pending Export, Exported, Duty Risk*), sin captura manual.

- **Alertas automáticas** a las áreas responsables cuando un pallet se acerca al vencimiento de su plazo legal, cuando hay faltantes o cuando se exceden los SLA internos.

- **Auditoría completa**: quién cambió qué dato, cuándo, y su valor anterior y nuevo. Autenticación con el usuario de Windows (Active Directory).

- **Sin integración con SAP en esta etapa:** el costo MM60 se carga desde el archivo de SAP, como hoy. La arquitectura deja la puerta abierta para conectarse en el futuro, pero esa conexión queda **fuera del alcance** actual.

## Beneficios esperados

| **Beneficio**                                    | **Indicador**                                                                      |
|--------------------------------------------------|------------------------------------------------------------------------------------|
| Visibilidad real del riesgo aduanal y financiero | Exposición USD y Duties to Pay por VS y periodo fiscal, sin falsos cierres         |
| Prevención de pagos de duty                      | Alertas a 60 y 30 días del vencimiento legal; lista diaria de pallets en Duty Risk |
| Datos confiables para Power BI                   | 0 archivos intermedios; vistas SQL estables                                        |
| Trazabilidad para auditorías                     | 100 % de los cambios registrados por usuario y campo                               |
| Edición simultánea sin riesgo                    | Se acaba la corrupción del archivo y la ruptura de rangos o columnas               |
| Base para la Fase Web                            | Las reglas y los datos se reutilizan al 100 %                                      |

## Plan y tiempos (estimación)

| **Fase**                            | **Duración estimada**                                                                  | **Resultado**                                               |
|-------------------------------------|----------------------------------------------------------------------------------------|-------------------------------------------------------------|
| 0 · Gobierno y validación de reglas | 2 semanas                                                                              | Registro en SOURCE, reglas firmadas por las áreas           |
| 1 · Fundaciones técnicas            | 4 semanas                                                                              | Base de datos, API y motor de cálculo probados              |
| 2 · MVP de escritorio               | 5 semanas                                                                              | Aplicación con los 6 bloques, permisos, auditoría y alertas |
| 3 · Migración y piloto VS9          | 2 semanas de corrida en paralelo con el Excel (la preparación arranca desde la Fase 1) | Histórico migrado y conciliado al centavo contra el Excel   |
| 4 · Go-Live VS1–VS9                 | 3 semanas de hypercare                                                                 | Excel en sólo lectura; la aplicación es la fuente oficial   |
| 5 · Versión Web                     | 6–8 semanas                                                                            | Acceso desde el navegador                                   |

Equipo estimado: 1 desarrollador .NET senior, 1 analista de proceso QMM a medio tiempo, y apoyo puntual de DBA/IT (servidor, SPN, Software Center) y de los Champions de cada VS para depurar el histórico. **Go-live estimado: ~4 meses después de aprobar la Fase 0.**

## Borrador de la aplicación (disponible)

Para recoger opiniones reales antes de construir la versión definitiva existe un **borrador funcional de escritorio** (.NET 8 / WPF, sin base de datos). Incluye:

- la bandeja de retornos;

- el formulario con los 6 bloques por área, con permisos por rol y Value Stream;

- el cálculo en vivo de indicadores y estatus;

- las validaciones;

- el tablero de Balance / Duties, las alertas, la matriz de Champions y la bitácora.

Puede **importar el Excel real en memoria (sólo lectura)** para ver los datos actuales con las reglas nuevas, y trae un botón para que cada revisor deje sus comentarios.

## Decisiones que se solicitan

1.  **Aprobar el alcance y la arquitectura** propuestos (escritorio primero, preparada para Web).

2.  **Nombrar al dueño del proceso y del dato** (ME/QMM-JuP1 · Process Excellence) para el registro en SOURCE.

3.  **Designar responsables** que confirmen los pendientes de negocio de la Sección 04 §8: plazo legal IMMEX, tasa de duty, catálogo de motivos, tipos de orden PPI y valuación del reflash.

4.  **Asignar recursos** (desarrollo, DBA/IT) y **confirmar el VS piloto** (se propone VS9).

5.  **Aprobar el congelamiento del Excel** (sólo lectura) en la fecha de go-live de cada VS.
