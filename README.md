# JuP1 · Expo & Return FG Follow Up (IB Process)

Digitalización de la **Matriz de Rastreo de Productos Terminados Importados Temporalmente 2.0**
Bosch Mobility Electronics · Planta Juárez 1 (JuP1, SAP 8640) · ME/QMM · Pilar Process Excellence (Q2E)

La matriz pasa de un Excel compartido en SharePoint a una aplicación C# (.NET 8) con base de datos Oracle centralizada. La lógica de negocio y el acceso a datos están separados de la interfaz de escritorio (WPF), para poder cambiar la interfaz sin reescribirlos. La integración con SAP queda **fuera del alcance** actual.

Contexto completo para retomar el trabajo (reglas, arquitectura, cómo compilar y probar, estado actual): [`CLAUDE.md`](CLAUDE.md).

## 1. Aplicación (.NET 8 / WPF) · v0.5.4

Solución `ExpoReturn.sln`. **Interfaz en inglés**, con los campos del Excel 2.0 (la columna "LOW export", que tenía dos datos, se dividió en número y fecha de expo).

* **Oracle obligatorio:** todo se guarda en las tablas `IB_PROCESS_` de `IFACTJUP`; no hay modo local ni datos de demostración. La app se conecta sola al abrir, con la cuenta de **SharedServersAccess.dll** y la clave integrada. **El usuario sólo necesita `ExpoReturn.Desktop.exe`**: el DLL va incrustado en el exe y no hay archivo de configuración (para compilar, el DLL se pone en `lib/`, que no se versiona). Si no puede, reintenta de inmediato, a los 5 s y a los 10 s, y no abre si todo falla. Catálogos, permisos por campo y parámetros se leen de la base.
* **IB PROCESS con varios pallets (v0.5.0):** cada registro es un **IB PROCESS** (encabezado: QMM Responsible, departamento solicitante, solicitante, motivo, detalle, fecha y factura de importación, cruce ELP → JuP1 y **Delivery Number**) con uno o más **pallets** (número de parte, cantidad, costo MM60, total SAP, HU **o** lote, fecha de PO, PO, ejecución MFO, exportación LOW, Customs Export, GR, HSE y Status Category). Cada pallet necesita su PO y su HU o lote; el pallet no se repite dentro del proceso, el PO sí. Al agregar un pallet se copian el PO y la fecha de PO del anterior. Si un PO ya está en otro IB PROCESS, la app pregunta *"Este PO ya se encuentra registrado en otro IB PROCESS desea continuar?"* (Cancelar regresa sin perder nada).
* **Estatus del proceso:** Draft sin pallets; Duty risk si algún pallet lo está; si no, el pallet abierto menos avanzado; con todos cerrados: Closed (duty paid) si alguno lo está, si no Exported si alguno lo está, si no Scrap confirmed.
* **Novedades v0.5.4 (sin cambios en la base de datos):** *My IB PROCESS* y *Pending for my area*; todos ven todos los VS en sólo lectura; la bandeja carga la tabla directo (sin botón) y la bitácora se lee **por páginas de 50** desde Oracle (antes sólo los últimos 5,000 movimientos); columnas que ya no se encogen; menú lateral plegable (**<** / **>** o Ctrl+B); fechas vacías con el formato dd/mm/yyyy; los reintentos de conexión muestran el error real (antes ORA-50099).
* **Permisos (v0.5.3): la requisición siempre la hace QMM.** **Owner** todo (también cerrados). **QMM** crea los IB PROCESS de su VS y llena la solicitud (encabezado y pallets): Champion / Proxy en todos los procesos del VS y también borran; Member en los que creó o de los que es QMM Responsible. **OSD, LOP, MFO, LOW y HSE** llenan sólo los campos de su área, en todos los procesos de su VS. Las seis áreas del formulario ponen el *Status Category* (columna COMMENTS del Excel) y escriben comentarios; el Member de QMM pone el *Status Category* sólo en sus procesos (los que creó o de los que es QMM Responsible). Los cerrados sólo los cambian Owner, Champion y Proxy. **MFQ y PUQ** (departamentos solicitantes, fuera del formulario) y los **Viewer** sólo consultan. **Todos ven todos los IB PROCESS de todos los VS**; donde no tienen permiso, en sólo lectura.
* **Pestañas por área, como el Excel:** 1 · QMM Request, 2 · QMM Pallets, 3 · OSD Customs, 4 · LOP Delivery, 5 · MFO Rework / Scrap, 6 · LOW Export, 7 · HSE Confirmation, 8 · Balance (calculado), 9 · Comments (Status Category y comentarios), 10 · Audit. La lista de pallets queda a la izquierda en todas; las pestañas de pallet trabajan sobre el pallet seleccionado. Ctrl+1 … Ctrl+9 y Ctrl+0.
* **Borrado:** D significa borrar de verdad procesos y pallets (con permiso y motivo); la bitácora guarda una copia completa (`SNAPSHOT`) y sigue siendo sólo de inserción. Un campo con valor se puede cambiar pero no dejar vacío (app y triggers). Quitar un acceso lo cierra con fecha.
* **My IB PROCESS y Pending for my area:** dos entradas del menú que abren la bandeja con un filtro: los procesos propios (creados, QMM Responsible o Requestor) y los pendientes del área del usuario en su VS (QMM = Draft; OSD = sin cruce o falta Customs Export tras la exportación de LOW; LOP = falta Delivery Number; MFO = piezas en piso o scrap sin GR; LOW = falta LOW export; HSE = falta confirmación). *Duty risk* sigue en *Alerts*. Se resuelven en SQL sobre las tablas: **no requieren cambios en la base**.
* **Paginación en la base (v0.5.1):** la app ya no carga todos los procesos. La bandeja pide a Oracle **una página de 50** (`OFFSET … FETCH`) y, aparte, los indicadores del filtro completo (`COUNT` / `SUM`). Filtros, búsqueda y orden se resuelven en SQL; el desglose ▸ trae sólo los pallets de esa página. *Balance / Duties* se calcula con `GROUP BY`, *Alerts* lee sólo los procesos con pallets abiertos y la pregunta del PO repetido es una consulta por PO. Al abrir un proceso se lee completo y al guardar se compara con la versión que hay en la base en ese momento (`ROW_VERSION`).
* **Datos siempre al día:** lo que depende de la fecha (vencimiento IMMEX, riesgo arancelario, estatus, aranceles con la tasa vigente) lo calculan las vistas con la fecha de hoy y los parámetros vigentes; no se toma de un valor guardado hace días. *Reload* vuelve a pedir la página.
* **Usuario real:** al abrir, la app lee el usuario de Windows. La primera vez pide nombre (ya viene escrito con el nombre que muestra Windows, sin la marca EXTERNAL ni el texto entre paréntesis; nunca con el usuario de Windows), Department y Value Stream(s); queda como **Viewer** con una solicitud de registro. El Owner la aprueba en la pantalla nueva **Requests** y el usuario pasa a **Member**. Al crear un proceso, si el usuario es de QMM queda como QMM Responsible; si no, como solicitante con su departamento (ambos se pueden cambiar eligiendo entre los usuarios registrados).
* **Departamentos:** sólo QMM, MFQ, PUQ, MFO, LOP, OSD (aduana, antes GS/OSD43), LOW y HSE; todos pueden solicitar.
* **Users & Access:** tabla User / Department / Value Stream / Function (Viewer, Member, Proxy, Champion, Owner). Sólo el **Owner** (`US\RLN8JU`) la modifica; *My profile* genera solicitudes que el Owner aprueba en **Requests**.
* **Import Excel 2.0 (Owner):** cada renglón del Excel queda como un IB PROCESS **editable** con un pallet. Los códigos de departamento se asignan por prefijo (MFQ4 → MFQ, MFO5 → MFO, LOW2 → LOW, GS/OSD43 → OSD) y el original queda en los hallazgos. Los errores que el Excel ya traía no impiden guardar; los nuevos sí. Una segunda importación no sobrescribe lo corregido.
* **Import MM60 file (Owner)** y la hoja MM60 del Excel se guardan en `IB_PROCESS_MM60_COSTS` y completan los costos faltantes; sin integración con SAP.

Instrucciones, guion de revisión y limitaciones en [`src/README.md`](src/README.md).

```bash
dotnet run --project src/ExpoReturn.Desktop
```

```bash
dotnet test
```

## 2. Base de datos Oracle (prefijo `IB_PROCESS_`)

**Reinstalación v0.5.0 (borra las tablas de la versión anterior):** con la cuenta dueña del esquema, en este orden:

1. [`00_drop_all.sql`](database/oracle/00_drop_all.sql): desactiva y borra el trigger `IB_PROCESS_NO_DROP` y después borra, una por una, **sólo** las tablas `IB_PROCESS_` que lista por nombre (las 19 de v0.4 y las 2 nuevas, si existieran), con sus datos. Ignora las que no existan. Termina con una consulta que debe devolver *no rows selected*.
2. [`IB_PROCESS_install.sql`](database/oracle/IB_PROCESS_install.sql) (abajo; incluye las vistas).
3. [`90_verify_install.sql`](database/oracle/90_verify_install.sql): secciones 1 a 4 sin filas.
4. Reemplazar el exe por el de v0.5.0 y, como Owner, **Import Excel 2.0…** con la MATRIZ DE RASTREO.

**Instalación (conectado con la cuenta dueña del esquema, la misma que entrega SharedServersAccess):** ejecutar [`IB_PROCESS_install.sql`](database/oracle/IB_PROCESS_install.sql). En SQL Developer, *Run Script* (F5); en SQL*Plus o SQLcl, `@IB_PROCESS_install.sql`. Se detiene en el primer error. Es la unión, en este orden, de:

1. [`01_create_tables.sql`](database/oracle/01_create_tables.sql): `CREATE TABLE` de las 21 tablas (catálogos, usuarios y acceso, solicitudes, permisos por campo, parámetros, **`IB_PROCESS_PROCESSES`** (encabezado) y **`IB_PROCESS_PALLETS`** (pallets, se borran con su proceso), historial de estatus, alertas, bitácora, **comentarios de los procesos** (`IB_PROCESS_COMMENTS`) y comentarios de la app), con sus llaves, índices y reglas `CHECK`.
2. [`02_seed_data.sql`](database/oracle/02_seed_data.sql): VS, departamentos, motivos, estatus, categorías de estatus y su lista por VS, permisos por campo, parámetros, contadores de folio y el primer Owner (`US\RLN8JU`).
3. [`03_comments_and_triggers.sql`](database/oracle/03_comments_and_triggers.sql): comentarios de tablas y columnas (nombre en la app y columna del Excel) y el trigger que impide modificar la bitácora.
4. [`04_integrity_rules.sql`](database/oracle/04_integrity_rules.sql): triggers que rechazan **vaciar un campo con valor**. Ya no hay triggers contra DELETE ni contra DROP (v0.5.0); sólo la bitácora sigue protegida contra cambios y borrado.
5. [`05_views.sql`](database/oracle/05_views.sql): las vistas que lee la app (no guardan datos). `IB_PROCESS_PARAMS_V` (parámetros vigentes), `IB_PROCESS_PALLETS_V` (un renglón por pallet con su proceso, vencimiento, riesgo y estatus calculados hoy) e `IB_PROCESS_INBOX_V` (un renglón por proceso con los totales de sus pallets y el estatus del proceso). Mismas reglas que la app.

Cómo encajan con la app:

* Las reglas `CHECK` de `IB_PROCESS_PROCESSES` / `IB_PROCESS_PALLETS` son **las mismas validaciones que bloquean el guardado en la app** (cada una indica su código VAL-xxx). Los avisos de la app (datos pendientes de un borrador, formato de HU, PO, lote o factura) no son reglas de la base. Así Oracle nunca rechaza algo que la app dejó guardar. El histórico del Excel (`IS_LEGACY = 'Y'`) conserva lo que traía.
* Los textos se miden en **caracteres** (`VARCHAR2(n CHAR)`), igual que la app: un acento o una "Ø" de SAP no hacen que un valor "no quepa".
* Los scripts son ASCII puro y no tienen `&` ni líneas en blanco dentro de las instrucciones (SQL*Plus las interpreta).
* Las pruebas automáticas revisan que los nombres midan como máximo 30 caracteres y que ninguno sea palabra reservada. También revisan que cada llave foránea apunte a una tabla ya creada y del mismo tipo, que la semilla use columnas existentes en el orden correcto, que los permisos sembrados sean los de la app y que cada columna de cada SQL de la app exista con el mismo largo.

**Cambios a una base ya instalada:** en `database/oracle/changes/`, uno por fecha; cada uno se ejecuta una vez y una instalación nueva ya lo incluye. Los scripts de cambio registran el usuario con dominio (`US\RLN8JU`), no `OS_USER`.

* [`2026-11-18_qmm_requests_and_comments.sql`](database/oracle/changes/2026-11-18_qmm_requests_and_comments.sql) (v0.5.3): quita los permisos del área REQUESTOR (MFQ / PUQ quedan de sólo lectura), da *Status Category* a OSD, LOP, MFO, LOW y HSE, y crea `IB_PROCESS_COMMENTS` con su índice y su trigger. No toca procesos, pallets, usuarios ni bitácora (sólo agrega una fila que registra el cambio). Termina con una consulta que debe mostrar 6, 0, 36, 1 y 1.

**Verificar la instalación (en cualquier momento):** [`90_verify_install.sql`](database/oracle/90_verify_install.sql) compara lo que de verdad hay en el esquema (diccionario de Oracle) con lo que definen los scripts: columnas con tipo, largo y nulos; constraints; índices y triggers. Sólo muestra **diferencias**: si las secciones 1 a 4 dicen *no rows selected*, el esquema es idéntico. También muestra las filas por tabla y los totales. Sólo hace `SELECT`.

Los scripts 02, 03, 04, el archivo único y el 90 se regeneran con `python3 tools/database/build_all.py`; el 01 y el 05 se editan a mano. El 90 también revisa que las tres vistas existan, compilen y respondan.

**Las vistas dan lo mismo que la app:** [`tools/database/check_views_parity.py`](tools/database/check_views_parity.py) ejecuta el SQL de `05_views.sql` y de `OracleProcessQueries` (traducido a DuckDB con sqlglot) sobre los datos que escribe la prueba `SqlParityDump`. Los valores se guardan en una fecha y se consultan meses después, y el resultado se compara con lo que calcula la app: renglones de la bandeja, pallets, filtros con orden, totales, balance y estatus. Con el Excel real: 658 procesos y 664 pallets idénticos, incluidos 20 pallets que pasaron a *Duty risk* sólo por el paso del tiempo.

Nombres de máximo 30 caracteres (compatibles con cualquier versión de Oracle); requiere Oracle 12c o superior por las columnas `IDENTITY`.

El **diccionario** [`database/Diccionario_Campos_ExpoReturn.xlsx`](database/Diccionario_Campos_ExpoReturn.xlsx) relaciona cada campo de la app con su columna en Oracle y su columna en cada hoja del Excel. Se genera desde [`tools/dictionary/fields.py`](tools/dictionary/fields.py) con `python3 tools/dictionary/build_field_dictionary.py` (la misma lista alimenta los comentarios de las columnas).

> Los valores marcados **[VALIDATE]** (plazo IMMEX, tasa de aranceles, F4/V1, HU/lote, Date PO) son provisionales hasta que los confirmen QMM y OSD.
