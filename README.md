# JuP1 · Expo & Return FG Follow Up (IB Process)

Digitalización de la **Matriz de Rastreo de Productos Terminados Importados Temporalmente 2.0**
Bosch Mobility Electronics · Planta Juárez 1 (JuP1, SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)

La matriz pasa de un Excel compartido en SharePoint a una aplicación C# (.NET 8) con SQL Server centralizado. La arquitectura es API-first, para que la interfaz de escritorio (WPF) se pueda reemplazar por una Web sin reescribir la lógica de negocio ni el modelo de datos. La integración con SAP queda **fuera del alcance** actual.

## 1. Documento del proyecto (fuente de verdad)

**[`entregables/SRS_ExpoReturn_FG_JuP1_v0.2.docx`](entregables/SRS_ExpoReturn_FG_JuP1_v0.2.docx)** es la versión vigente y **la fuente de verdad**. Las revisiones (comentarios y control de cambios) se hacen en el Word.

Los archivos de `docs/` son la copia de trabajo **derivada** del Word (`python3 tools/docx/docx_to_md.py`); no se editan a mano. Flujo completo en [`tools/docx/README.md`](tools/docx/README.md).

| Capítulo (derivado) | Contenido |
|---|---|
| [Resumen Ejecutivo](docs/Resumen_Ejecutivo.md) | Problema con cifras del Excel real, propuesta, beneficios, plan y decisiones solicitadas |
| [00 · Diagnóstico AS-IS](docs/00_Diagnostico_AS-IS_Matriz_2.0.md) | Análisis del archivo real: 625 registros, ≈ USD 7.4 M importados, 17 hallazgos (VS7 lee los datos de VS4: ≈ USD 700 k no visibles; BALANCE en error; falsos cierres) |
| [01 · Arquitectura](docs/01_Arquitectura_Solucion.md) | ADRs, capas, solución .NET, API, seguridad/RBAC, auditoría, despliegue y borrador implementado |
| [02 · Modelo ER](docs/02_Modelo_Entidad_Relacion.md) | Diagrama ER, diccionario de datos y mapeo Excel → SQL |
| [03 · Reglas de negocio](docs/03_Reglas_de_Negocio.md) | Validaciones VAL-xxx, motor financiero, máquina de estados, Duty Risk, alertas y casos golden |
| [04 · Plan de implementación](docs/04_Plan_Migracion_Implementacion.md) | Fases, migración del histórico, cutover, UAT, riesgos y pendientes de validación (P-01…P-12) |
| [Comentarios de revisión](docs/_revision_comentarios.md) | Comentarios exportados del Word |

## 2. Borrador de la aplicación (.NET 8 / WPF)

Solución `ExpoReturn.sln` con las pantallas esperadas, **sin base de datos**: bandeja, formulario de 6 bloques con permisos por rol/VS, cálculo en vivo, validaciones, Balance/Duties, alertas, Champions y bitácora. Puede importar el Excel real **en memoria y en sólo lectura**.

Instrucciones, guion de revisión y limitaciones en [`src/README.md`](src/README.md).

```bash
dotnet run --project src/ExpoReturn.Desktop
```

```bash
dotnet test
```

## 3. Base de datos (diseño para producción, SQL Server 2019+)

1. [`database/01_schema.sql`](database/01_schema.sql): esquemas, catálogos, RBAC, tabla transaccional temporal, auditoría append-only
2. [`database/02_seed_catalogs.sql`](database/02_seed_catalogs.sql): VS, departamentos, 10 motivos, estatus, categorías, roles, permisos por campo y parámetros
3. [`database/03_views_powerbi.sql`](database/03_views_powerbi.sql): vistas `rpt.*` para Power BI
4. [`database/04_staging_migration.sql`](database/04_staging_migration.sql): staging y transformación del histórico Excel

> Los valores marcados **[VALIDAR]** (plazo IMMEX, tasa de duty, motivos, F4/V1, HU/lote, Date PO, matriz Champions) son provisionales hasta que los confirmen QMM y GS/OSD43. Ver Sección 04 §8.
