# JuP1 · Expo & Return FG Follow Up (IB Process)

Digitalización de la **Matriz de Rastreo de Productos Terminados Importados Temporalmente 2.0**
Bosch Mobility Electronics · Planta Juárez 1 (JuP1, SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)

La matriz pasa de un Excel compartido en SharePoint a una aplicación C# (.NET 8) con SQL Server centralizado. La arquitectura es API-first, para que la interfaz de escritorio (WPF) se pueda reemplazar por una Web sin reescribir la lógica de negocio ni el modelo de datos.

## Documento del proyecto (SRS y diseño técnico)

| # | Documento | Contenido |
|---|---|---|
| 00 | [Diagnóstico AS-IS de la Matriz 2.0](docs/00_Diagnostico_AS-IS_Matriz_2.0.md) | Estructura del Excel, fórmulas actuales y 17 hallazgos (incluye fórmulas de VS7 que apuntan a VS_4, falsos cierres y faltantes que no aparecen en el balance) |
| 01 | [Arquitectura de la Solución (Desktop → Web)](docs/01_Arquitectura_Solucion.md) | ADRs, capas Clean Architecture, estructura de la solución .NET, API, seguridad/RBAC, auditoría, SAP NCo, SOURCE/BBM, despliegue |
| 02 | [Modelo Entidad-Relación](docs/02_Modelo_Entidad_Relacion.md) | Diagrama ER, diccionario de datos y mapeo columna por columna Excel → SQL |
| 03 | [Reglas de Negocio y Fórmulas](docs/03_Reglas_de_Negocio.md) | Catálogo de validaciones VAL-xxx, motor financiero v1 (réplica Excel) y v2 (corregido), máquina de estados, Duty Risk, alertas, casos golden |
| 04 | [Plan de Migración e Implementación](docs/04_Plan_Migracion_Implementacion.md) | Fases MVP → Producción → Web, ETL del histórico, conciliación, runbook de cutover, UAT, riesgos y pendientes |

## Scripts de base de datos (SQL Server 2019+)

Se ejecutan en orden sobre una BD vacía (p. ej. `JUP1_QMM_EXPORETURN`):

1. [`database/01_schema.sql`](database/01_schema.sql): esquemas, catálogos, RBAC, tabla transaccional temporal, auditoría append-only, roles de BD
2. [`database/02_seed_catalogs.sql`](database/02_seed_catalogs.sql): VS, departamentos, 10 motivos, estatus, categorías, roles, permisos por campo y parámetros
3. [`database/03_views_powerbi.sql`](database/03_views_powerbi.sql): vistas `rpt.*` para Power BI (BALANCE_ por periodo fiscal, conciliación de cantidades, Duty Risk, audit trail)
4. [`database/04_staging_migration.sql`](database/04_staging_migration.sql): staging, parsers y transformación del histórico Excel

> Los valores marcados **[VALIDAR]** (plazo IMMEX, tasa de duty, catálogo de motivos, PPI5/PPI6) son provisionales hasta que los confirmen QMM y GS/OSD43. Ver [04 §8](docs/04_Plan_Migracion_Implementacion.md#8-pendientes-de-validación-del-negocio-consolidado).
