# JuP1 · Expo & Return FG Follow Up (IB Process)

Digitalización de la **Matriz de Rastreo de Productos Terminados Importados Temporalmente 2.0**
Bosch Mobility Electronics · Planta Juárez 1 (JuP1, SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)

La matriz pasa de un Excel compartido en SharePoint a una aplicación C# (.NET 8) con SQL Server centralizado. La arquitectura es API-first, para que la interfaz de escritorio (WPF) se pueda reemplazar por una Web sin reescribir la lógica de negocio ni el modelo de datos. La integración con SAP queda **fuera del alcance** actual.

## 1. Documento del proyecto (fuente de verdad)

**[`entregables/SRS_ExpoReturn_FG_JuP1_v0.4.docx`](entregables/SRS_ExpoReturn_FG_JuP1_v0.4.docx)** es la versión vigente y **la fuente de verdad**. Las revisiones (comentarios y control de cambios) se hacen en el Word.

Los archivos de `docs/` son la copia de trabajo **derivada** del Word (`python3 tools/docx/docx_to_md.py`); no se editan a mano. Flujo completo en [`tools/docx/README.md`](tools/docx/README.md).

| Capítulo (derivado) | Contenido |
|---|---|
| [Resumen Ejecutivo](docs/Resumen_Ejecutivo.md) | Contexto, problema, propuesta y beneficios esperados |
| [01 · Arquitectura](docs/01_Arquitectura_Solucion.md) | ADRs, capas, solución .NET, API, seguridad/RBAC, auditoría, despliegue y borrador implementado |
| [02 · Modelo ER](docs/02_Modelo_Entidad_Relacion.md) | Diagrama ER, diccionario de datos y mapeo Excel → SQL |
| [03 · Reglas de negocio](docs/03_Reglas_de_Negocio.md) | Validaciones VAL-xxx, motor financiero, máquina de estados, riesgo arancelario, alertas y casos golden |
| [Comentarios de revisión](docs/_revision_comentarios.md) | Comentarios exportados del Word |

## 2. Manual de pantallas y control de UX

**[`entregables/UX_Manual_Pantallas_ExpoReturn_v0.3.docx`](entregables/UX_Manual_Pantallas_ExpoReturn_v0.3.docx)** (fuente de verdad, en horizontal) contiene:

* las fichas de las 8 pantallas, con capturas anotadas y la tabla de elementos;
* la lista de estatus del pallet y su significado para el usuario;
* los flujos por rol;
* el registro de hallazgos UX de las rondas 1 a 4 (capturas de la app v0.1.5), el estado de los pendientes UX y la lista de verificación.

Copia derivada en [`docs/ux/`](docs/ux/). Capturas por versión en `docs/ux/capturas/` y marcadores en `tools/ux/`.

## 3. Borrador de la aplicación (.NET 8 / WPF)

Solución `ExpoReturn.sln` con las pantallas esperadas, **sin base de datos**: bandeja, formulario de 6 bloques con permisos por rol/VS, cálculo en vivo, validaciones, Balance/Aranceles, alertas, Champions y bitácora. Puede importar el Excel real **en memoria y en sólo lectura**.

Instrucciones, guion de revisión y limitaciones en [`src/README.md`](src/README.md).

```bash
dotnet run --project src/ExpoReturn.Desktop
```

```bash
dotnet test
```

## 4. Base de datos (diseño para producción, SQL Server 2019+)

Estructura alineada con la app v0.1.7: sólo los campos de la Matriz 2.0, con nombres limpios. El **diccionario** [`entregables/Diccionario_Campos_ExpoReturn.xlsx`](entregables/Diccionario_Campos_ExpoReturn.xlsx) relaciona cada campo de la app con su columna en la BD y su encabezado y columna en cada hoja del Excel (se regenera con `python3 tools/dictionary/build_field_dictionary.py`).

1. [`database/01_schema.sql`](database/01_schema.sql): esquemas, catálogos (incluye las opciones de categoría de estatus por VS), RBAC, tabla transaccional temporal, alertas con atención, auditoría append-only
2. [`database/02_seed_catalogs.sql`](database/02_seed_catalogs.sql): VS, departamentos, 10 motivos, estatus, categorías de estatus (texto exacto del Excel) y su lista por VS, roles, permisos por campo y parámetros
3. [`database/03_views_powerbi.sql`](database/03_views_powerbi.sql): vistas `rpt.*` para Power BI
4. [`database/04_staging_migration.sql`](database/04_staging_migration.sql): staging y transformación del histórico Excel

> Los valores marcados **[VALIDAR]** (plazo IMMEX, tasa de duty, motivos, F4/V1, HU/lote, Date PO, matriz Champions) son provisionales hasta que los confirmen QMM y GS/OSD43. Ver SRS, capítulo 03.
