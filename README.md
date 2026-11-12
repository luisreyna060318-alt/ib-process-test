# JuP1 · Expo & Return FG Follow Up (IB Process)

Digitalización de la **Matriz de Rastreo de Productos Terminados Importados Temporalmente 2.0**
Bosch Mobility Electronics · Planta Juárez 1 (JuP1, SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)

La matriz pasa de un Excel compartido en SharePoint a una aplicación C# (.NET 8) con base de datos Oracle centralizada. La arquitectura es API-first, para que la interfaz de escritorio (WPF) se pueda reemplazar por una Web sin reescribir la lógica de negocio ni el modelo de datos. La integración con SAP queda **fuera del alcance** actual.

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

## 3. Aplicación (.NET 8 / WPF) · v0.3.3

Solución `ExpoReturn.sln`. **Interfaz en inglés**, con los campos del Excel 2.0 (la columna "LOW export", que tenía dos datos, se dividió en número y fecha de expo).

* **Oracle:** todas las operaciones escriben en las tablas `IB_PROCESS_` de `IFACTJUP`. La cuenta se obtiene con **SharedServersAccess.dll** (igual que el worker de good-receipt) y la clave maestra se toma de `SHARED_SERVERS_CLAVE` o se pide al abrir; no se guarda. El DLL se copia junto al exe y no se versiona.
* **Reglas de datos, también para el Owner:** nunca se borra nada (los accesos que terminan se cierran con fecha, las solicitudes quedan aprobadas o rechazadas). Un campo con valor se puede cambiar pero no dejar vacío. Lo valida la app y lo impide la base de datos con triggers.
* **Usuario real:** al abrir, la app lee el usuario de Windows. La primera vez pide nombre, Department y Value Stream(s); la función inicial es **Viewer** (sólo lectura).
* **Departamentos:** lista nueva (MFQ8, MFO1, MFO12, MFO1-US, MFE5, QMM1, QMM2, LOP1, LOP2, LOW, CTG3, TEF6, GS/OSD43, HSE y, en digitalización y proyectos transversales, QMM-L2, DBE y TEF1). Los códigos anteriores quedan inactivos sólo para el histórico.
* **Users & Access:** tabla User / Department / Value Stream / Function (Viewer, Proxy, Champion, Owner). Sólo el **Owner** (`US\RLN8JU`) la modifica; *My profile* genera solicitudes que el Owner aprueba.
* **Import Excel 2.0 (Owner):** cada renglón del Excel queda como registro **editable** en la base para homogeneizar los datos. Los errores que el Excel ya traía no impiden guardar; los nuevos sí. Una segunda importación no sobrescribe lo corregido.
* **Import MM60 file (Owner)** y la hoja MM60 del Excel se guardan en `IB_PROCESS_MM60_COSTS` y completan los costos faltantes; sin integración con SAP.
* Modo **Local** (demo en memoria) si no hay conexión.

Instrucciones, guion de revisión y limitaciones en [`src/README.md`](src/README.md).

```bash
dotnet run --project src/ExpoReturn.Desktop
```

```bash
dotnet test
```

## 4. Base de datos Oracle (prefijo `IB_PROCESS_`)

**Instalación (una sola vez, conectado con la cuenta dueña del esquema, la misma que entrega SharedServersAccess):** ejecutar [`IB_PROCESS_install.sql`](database/oracle/IB_PROCESS_install.sql). En SQL Developer, *Run Script* (F5); en SQL*Plus o SQLcl, `@IB_PROCESS_install.sql`. Se detiene en el primer error. Es la unión, en este orden, de:

1. [`01_create_tables.sql`](database/oracle/01_create_tables.sql): `CREATE TABLE` de las 19 tablas (catálogos, usuarios y acceso, solicitudes de perfil, permisos por campo, parámetros, pallets, historial de estatus, alertas, bitácora y comentarios), con sus llaves, índices y reglas `CHECK`.
2. [`02_seed_data.sql`](database/oracle/02_seed_data.sql): VS, departamentos, motivos, estatus, categorías de estatus y su lista por VS, permisos por campo, parámetros, contadores de folio y el primer Owner (`US\RLN8JU`).
3. [`03_comments_and_triggers.sql`](database/oracle/03_comments_and_triggers.sql): comentarios de tablas y columnas (nombre en la app y columna del Excel) y el trigger que impide modificar la bitácora.
4. [`04_integrity_rules.sql`](database/oracle/04_integrity_rules.sql): en cada tabla, un trigger que **rechaza DELETE** y otro que rechaza **vaciar un campo con valor**; además impide `DROP` / `TRUNCATE` de los objetos `IB_`. Para mantenimiento planeado: `ALTER TRIGGER … DISABLE` / `ENABLE`.

Cómo encajan con la app:

* Las reglas `CHECK` de `IB_PROCESS_PALLET_RETURNS` son **las mismas validaciones que bloquean el guardado en la app** (cada una indica su código VAL-xxx). Los avisos de la app (datos pendientes de un borrador, formato de HU, PO, lote o factura) no son reglas de la base. Así Oracle nunca rechaza algo que la app dejó guardar. El histórico del Excel (`IS_LEGACY = 'Y'`) conserva lo que traía.
* Los textos se miden en **caracteres** (`VARCHAR2(n CHAR)`), igual que la app: un acento o una "Ø" de SAP no hacen que un valor "no quepa".
* Los scripts son ASCII puro y no tienen `&` ni líneas en blanco dentro de las instrucciones (SQL*Plus las interpreta).
* Las pruebas automáticas revisan que los nombres midan como máximo 30 caracteres y que ninguno sea palabra reservada. También revisan que cada llave foránea apunte a una tabla ya creada y del mismo tipo, que la semilla use columnas existentes en el orden correcto, que los permisos sembrados sean los de la app y que cada columna de cada SQL de la app exista con el mismo largo.

**Verificar la instalación (en cualquier momento):** [`90_verify_install.sql`](database/oracle/90_verify_install.sql) compara lo que de verdad hay en el esquema (diccionario de Oracle) con lo que definen los scripts: columnas con tipo, largo y nulos; constraints; índices y triggers. Sólo muestra **diferencias**: si las secciones 1 a 4 dicen *no rows selected*, el esquema es idéntico. También muestra las filas por tabla y los totales. Sólo hace `SELECT`.

Los scripts 02, 03, 04, el archivo único y el 90 se regeneran con `python3 tools/database/build_all.py`; el 01 se edita a mano.

Nombres de máximo 30 caracteres (compatibles con cualquier versión de Oracle); requiere Oracle 12c o superior por las columnas `IDENTITY`.

El **diccionario** [`entregables/Diccionario_Campos_ExpoReturn.xlsx`](entregables/Diccionario_Campos_ExpoReturn.xlsx) relaciona cada campo de la app con su columna en Oracle y su columna en cada hoja del Excel. Se genera desde [`tools/dictionary/fields.py`](tools/dictionary/fields.py) con `python3 tools/dictionary/build_field_dictionary.py` (la misma lista alimenta los comentarios de las columnas).

> Los valores marcados **[VALIDATE]** (plazo IMMEX, tasa de aranceles, F4/V1, HU/lote, Date PO) son provisionales hasta que los confirmen QMM y GS/OSD43.
