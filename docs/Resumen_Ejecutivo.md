<!-- GENERADO desde entregables/SRS_ExpoReturn_FG_JuP1_v0.4.docx el 2026-11-06 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# Resumen Ejecutivo


## Contexto

Cuando un pallet de producto terminado de JuP1 se retorna desde El Paso WHS para retrabajo, sorteo o reflasheo (Inhouse Repair / IB Process), entra a México como **importación temporal bajo IMMEX**. Cada pieza importada debe conciliarse al 100 %: con su **re-exportación** a El Paso o con su **destrucción comprobada** por HSE. Si la conciliación falla, la planta paga aranceles e impuestos (*Duties to Pay*) y absorbe la pérdida de inventario (*MFO-LOW Failure*).

Hoy este control vive en un archivo de Excel compartido en SharePoint (**MATRIZ DE RASTREO 2.0**), con una hoja por Value Stream (VS1, VS4, VS5, VS7, VS9), en el que capturan siete áreas: QMM, MFO, MFQ, LOP, LOW, GS/OSD43 y HSE.

## Problema

El control en Excel tiene problemas de gobernanza que afectan la confiabilidad de las cifras y el cumplimiento:

| **Problema**                                                                                                             | **Efecto en el negocio**                                                        |
|--------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|
| Edición simultánea de varios Champions y Proxies sobre un archivo compartido                                             | Riesgo de corrupción del archivo y de pérdida de cambios                        |
| Fórmulas y rangos de tabla que se rompen al insertar filas o columnas; columnas financieras que se ocultan por accidente | Indicadores financieros y de aranceles poco confiables                          |
| Captura sin validación estricta (texto libre en departamentos, nombres, motivos y fechas)                                | No se puede medir la antigüedad IMMEX ni analizar por motivo o área en Power BI |
| Cierres capturados como texto, sin evidencia documental (pedimento, confirmación de HSE)                                 | Pallets que parecen conciliados sin estarlo                                     |
| Sin bitácora de cambios, sin control por área y sin alertas de vencimiento                                               | No hay trazabilidad para una auditoría aduanal ni detección temprana del riesgo |

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
