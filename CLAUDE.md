# CLAUDE.md · Contexto del proyecto (para retomar en otra sesión)

**Expo & Return FG Follow Up (IB Process)**: aplicación de escritorio Windows (.NET 8, WPF) con base Oracle que sustituye
al Excel *"Productos terminados importados temporalmente · Matriz de rastreo 2.0"* de Bosch JuP1 (planta Juárez 1,
área ME/QMM). Versión actual: **v0.5.4**. El usuario (dueño del proceso, cuenta Windows `US\RLN8JU`, rol **Owner**) habla
español: **responder en español**. La interfaz y el código están en inglés; los README en español.

## 1. Qué hace el negocio (glosario mínimo)

* **IB PROCESS**: una devolución de producto terminado de El Paso a JuP1. Es un **encabezado** (quién pide, motivo,
  entrada aduanal, delivery) con **uno o más pallets**. Folio `VS7-00012`; pallet `VS7-00012-01`.
* **Pallet**: número de parte, cantidad, HU **o** lote, PO y fecha de PO (obligatorios: PO y HU o lote; un pallet no se
  repite en su proceso; el PO sí puede repetirse y, si ya está en otro proceso, la app pregunta *"Este PO ya se encuentra
  registrado en otro IB PROCESS desea continuar?"*).
* **VS** (Value Stream): 1, 4, 5, 7, 9. Cada proceso pertenece a uno; los permisos son por VS.
* **IMMEX**: importación temporal; plazo legal 540 días desde el cruce ELP → JuP1. Si se vence o faltan piezas hay
  **riesgo arancelario** (aranceles = falla MFO-LOW × 0.19). Parámetros en `IB_PROCESS_PARAMETERS`.
* **Áreas del formulario** (cada una llena sus columnas del Excel):
  QMM = la requisición (encabezado + pallets) · OSD (aduana, antes "GS/OSD43") = fecha y factura de importación, cruce
  ELP-JuP1, Customs Export · LOP = Delivery Number · MFO = retrabajo y scrap + GR · LOW = LOW export (número y fecha) ·
  HSE = kg y fecha de confirmación. **MFQ y PUQ** (departamentos solicitantes) solo leen.
* **Departamentos**: solo QMM, MFQ, PUQ, MFO, LOP, OSD, LOW, HSE. Los códigos viejos del Excel (MFQ4, GS/OSD43…) se
  asignan por prefijo al importar y el original queda en los hallazgos.

## 2. Reglas que no se deben romper

* **Funciones** (`UserFunction`): Viewer < Member < Proxy < Champion < Owner. Registro nuevo = Viewer + solicitud; el
  Owner aprueba en *Requests* → Member.
* **Permisos** (`FieldPermissions` en `src/ExpoReturn.Application/Security.cs`, matriz también en
  `IB_PROCESS_FIELD_PERMISSIONS`): la requisición **siempre la hace QMM**. QMM crea; Champion/Proxy de QMM editan la
  solicitud de todo su VS y borran; Member de QMM solo la de los procesos que creó o de los que es QMM Responsible.
  OSD/LOP/MFO/LOW/HSE editan solo sus campos en todo su VS. Las seis áreas ponen *Status Category* (columna COMMENTS del
  Excel) y escriben comentarios; el Member de QMM pone *Status Category* solo en sus procesos (decisión del usuario,
  2026-11-19: "por algo es el QMM Responsible"). Cerrados: solo Owner, Champion, Proxy. MFQ/PUQ y Viewer: solo lectura.
* **Lectura:** todos ven todos los procesos de todos los VS (decisión 2026-11-19); `FieldPermissions.CanRead` siempre es true.
* Un campo con valor **se puede cambiar pero nunca dejar vacío** (app + triggers `*_NO_EMPTY`).
* **Borrar** procesos/pallets sí se permite (Owner y Champion/Proxy de QMM), con motivo; la bitácora
  (`IB_PROCESS_AUDIT_LOG`, solo inserción) guarda una copia JSON (`SNAPSHOT`), incluidos los comentarios.
* Quitar un acceso lo **cierra con fecha** (no se borra). Concurrencia optimista con `ROW_VERSION` (encabezado y cada pallet).
* **Estatus del proceso**: Draft sin pallets; Duty risk si algún pallet lo está; si no, el pallet abierto menos avanzado;
  todos cerrados → Closed (duty paid) / Exported / Scrapped. Lo que depende de la fecha se calcula al consultar.
* Los registros importados del Excel (`IS_LEGACY = 'Y'`) son editables; los errores que ya traían se muestran como avisos.

## 3. Mapa del repositorio

| Ruta | Qué es |
|---|---|
| `src/ExpoReturn.Domain` | `IbProcess`, `PalletReturn`, catálogos, `Rules/` (validaciones VAL-xxx, cálculo financiero, estatus, riesgo) |
| `src/ExpoReturn.Application` | `ProcessService` (guardar/borrar/comentar), `Security.cs` (permisos), `Queries.cs` (contratos de bandeja paginada), `Reporting.cs` (balance y alertas) |
| `src/ExpoReturn.Infrastructure.Oracle` | ODP.NET administrado: repositorio sin caché, `OracleProcessQueries` (SQL sobre las vistas, `OFFSET … FETCH`), usuarios, bitácora, comentarios, `SharedServersCredentials` |
| `src/ExpoReturn.Infrastructure.InMemory` | Implementaciones en memoria (solo pruebas), importador del Excel 2.0, exportador a Excel, MM60 |
| `src/ExpoReturn.Desktop` | WPF + MVVM (CommunityToolkit.Mvvm). `AppServices.cs` arma todo; `ViewModels/`, `Views/`, `Themes/Theme.xaml`, `FieldHelp.cs` (tooltips) |
| `tests/ExpoReturn.Domain.Tests` | xUnit (156 pruebas): reglas, permisos, paginación, coherencia código ↔ scripts SQL |
| `database/oracle` | Scripts (ver §5). `database/Diccionario_Campos_ExpoReturn.xlsx` = campo ↔ columna Oracle ↔ columna del Excel |
| `tools/database` | Generadores de scripts (`build_all.py`) y `check_views_parity.py` (vistas en DuckDB vs. app) |
| `tools/dictionary` | Lista de campos y generador del diccionario (la usan también los generadores SQL) |
| `README.md`, `src/README.md` | Documentación para el usuario (instalación, pantallas, guion de revisión) |

## 4. Compilar, ejecutar y probar (Windows)

* SDK .NET 8. `dotnet build ExpoReturn.sln` · `dotnet test` · `dotnet run --project src/ExpoReturn.Desktop`.
* **`lib/SharedServersAccess.dll` es obligatorio para compilar el Desktop** y **no está en el repo** (lo da el
  usuario; `.gitignore`). Se incrusta en el exe. La clave maestra está en el código (`OracleSettings`).
* **No hay modo local ni demo**: al abrir, la app se conecta a Oracle `IFACTJUP` (esquema `JUP1QMM`) con la cuenta que
  entrega el DLL; necesita la red de planta y `tnsnames.ora` (`TNS_ADMIN` o `%ORACLE_HOME%\network\admin`). Sin conexión
  muestra la ventana *Could not connect* (3 reintentos) y no abre. Las clases InMemory existen solo para las pruebas.
* Prueba con el Excel real (no se versiona; pedirlo al usuario): `set EXPORETURN_LEGACY_XLSX=C:\ruta\archivo.xlsx`.
* Paridad de vistas: `set EXPORETURN_SQL_PARITY_DIR=C:\tmp\parity` + `dotnet test --filter SqlParityDump`, luego
  `pip install duckdb sqlglot` y `python tools/database/check_views_parity.py C:\tmp\parity` (debe decir OK).
* Publicar (exe único autocontenido):
  `dotnet publish src/ExpoReturn.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish`
  Se entrega un zip con el exe + `LEEME.md` (copia de `src/README.md`) + los scripts que apliquen.
* Versión: subir `AppConfig.Version` (`src/ExpoReturn.Desktop/AppConfig.cs`) y `<Version>` de `Directory.Build.props`
  y los títulos de los README.

## 5. Base de datos (Oracle, prefijo `IB_PROCESS_`)

* 21 tablas (catálogos, usuarios/accesos/solicitudes, permisos por campo, parámetros, `PROCESSES`, `PALLETS`, historial,
  alertas, bitácora, comentarios de procesos, feedback) y 3 vistas (`PARAMS_V`, `PALLETS_V`, `INBOX_V`) que calculan
  con `TRUNC(SYSDATE)` vencimiento, riesgo, estatus y aranceles.
* Scripts: `00_drop_all.sql` (borra solo los objetos listados por nombre), `IB_PROCESS_install.sql` (= 01 + 02 + 03 +
  04 + 05), `90_verify_install.sql` (solo SELECT; secciones 1–4b sin filas = esquema correcto).
  `01_create_tables.sql` y `05_views.sql` se editan a mano; **02, 03, 04, install y 90 se regeneran** con
  `python tools/database/build_all.py` (y el diccionario con `python tools/dictionary/build_field_dictionary.py`).
* Reglas de los scripts: ASCII puro, sin `&`, sin líneas en blanco dentro de una instrucción, `VARCHAR2(n CHAR)`, nombres
  ≤ 30, sin palabras reservadas como columnas o binds (ORA-01745). Las pruebas lo revisan.
* **Base ya instalada → cambios en `database/oracle/changes/AAAA-MM-DD_*.sql`**, uno por fecha, que no borren datos y
  que registren al usuario **con dominio `US\RLN8JU`** (nunca `OS_USER`). Agregar una prueba de que el cambio deja la
  base igual que una instalación nueva. Enviar los scripts al usuario para que los revise antes de ejecutarlos.

* **My IB PROCESS / Pending for my area** (acordado 2026-11-19): `ProcessFilter.MineOf` / `PendingFor` + `PendingWork`
  (`src/ExpoReturn.Application/PendingWork.cs`, misma regla en C# y en `PendingWork.SqlCondition`). Duty risk NO es
  pendiente (queda en Alerts). Sin cambios de esquema.

## 6. Estado actual y lo que falta verificar

* **v0.5.4 (2026-11-19), probada en Windows contra Oracle Free 23 en Docker** (entorno local fuera del repo, carpeta
  `test-env` junto al repo: DLL falso de SharedServersAccess, `run-app.ps1`, bancos `RoleTests` y `UiSnap`): instalación
  limpia + `90_verify` sin diferencias (primera vez en Oracle real), importación del Excel real (633 filas, 0 rechazos),
  permisos por rol (223/224, la diferencia era la regla de Status Category ya resuelta) e interfaz por usuario (462/462).
  **No requiere cambios de base** respecto a v0.5.3 (sí requiere el cambio 2026-11-18, ver abajo).

* El usuario reinstaló la base con los scripts de **v0.5.1** y `90_verify_install.sql` salió correcto.
* Se le entregó `changes/2026-11-18_qmm_requests_and_comments.sql` (v0.5.2: permisos de QMM + tabla de comentarios);
  **confirmar si ya lo ejecutó** (debe existir `IB_PROCESS_COMMENTS`; la consulta final del script da 6, 0, 36, 1, 1).
* **La interfaz nunca se ha visto en Windows** (se desarrolló en Linux): revisar al abrir la app el acomodo de las 10
  pestañas del detalle (QMM Request, QMM Pallets, OSD Customs, LOP Delivery, MFO Rework / Scrap, LOW Export,
  HSE Confirmation, Balance, Comments, Audit), la lista de pallets a la izquierda, la bandeja paginada con desglose ▸,
  *Requests*, *Users & Access* y el nombre de Windows en *Welcome*. Luego, como Owner, **Import Excel 2.0…** con el Excel real.
* Las vistas SQL solo se ejecutaron en DuckDB (paridad OK con 658 procesos del Excel real), no en Oracle.
* Usuarios aún no usan la app.

## 7. Forma de trabajar con este usuario

* Explicar en español claro, sin jerga; dar instrucciones paso a paso para lo que él ejecuta (scripts, exe).
* Antes de cualquier operación destructiva en la base, enseñarle exactamente qué objetos toca y esperar su visto bueno.
* No subir al repo: el Excel real, `SharedServersAccess.dll`, ni datos personales. No poner identificadores de modelo en
  commits. Rama de trabajo: la indicada en la sesión; no crear PR si no lo pide.
