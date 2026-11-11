# Aplicación (v0.3.1): Expo & Return FG Follow Up

Aplicación de escritorio con **interfaz en inglés**. Respeta la arquitectura del SRS (capítulo 01): Domain → Application → Infrastructure → Desktop.

Tiene dos modos (`"DataSource"` en `appsettings.json`):

* **Oracle** (por defecto): pallets, usuarios, accesos, solicitudes, bitácora, alertas atendidas, maestro MM60, parámetros y comentarios se guardan en las tablas `IB_PROCESS_` de `IFACTJUP`. La cuenta de Oracle se obtiene con **SharedServersAccess.dll**, igual que el worker de good-receipt.
* **Local**: demo en memoria (los pallets se pierden al cerrar) con usuarios en un archivo JSON. La app lo ofrece si no puede abrir Oracle.

**Reglas de datos (también para el Owner):** nunca se borra nada. Un campo que ya tiene valor se puede modificar, pero no dejar vacío. Lo cumplen la app y la base de datos (triggers de `04_integrity_rules.sql`).

## Cómo abrirlo

**Opción A · sin instalar nada (revisores):** ejecutar `ExpoReturn.Desktop.exe`, la versión autocontenida para Windows 10/11 x64 que se comparte aparte; no requiere .NET.

**Opción B · desde el código (desarrollo):** con el SDK de .NET 8 en Windows:

```bash
dotnet run --project src/ExpoReturn.Desktop
```

O abrir `ExpoReturn.sln` en Visual Studio 2022 (17.8 o superior) y ejecutar `ExpoReturn.Desktop`.

Para generar el ejecutable autocontenido:

```bash
dotnet publish src/ExpoReturn.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Pruebas automáticas: corren en Windows o Linux.

```bash
dotnet test
```

Para incluir la prueba de importación del Excel real, apunte la variable de entorno al archivo (el Excel **no** se versiona):

```bash
set EXPORETURN_LEGACY_XLSX=C:\ruta\PRODUCTOS_TERMINADOS_IMPORTADOS_TEMPORALMENTE_MATRIZ_DE_RASTREO_2.0.xlsx
```

```bash
dotnet test
```

## Conexión a Oracle (SharedServersAccess.dll)

1. Crear las tablas ejecutando una vez `database/oracle/IB_PROCESS_install.sql` con la cuenta dueña del esquema (ver el README principal).
2. Copiar **`SharedServersAccess.dll`** junto a `ExpoReturn.Desktop.exe`. No se incluye en el repositorio ni en el zip.
3. `tnsnames.ora`: la app resuelve el alias `IFACTJUP` como el worker de Python, agregando `NAMES.DEFAULT_DOMAIN` de `sqlnet.ora`. Busca la carpeta en `"TnsAdmin"`, luego en la variable `TNS_ADMIN` y luego en `%ORACLE_HOME%\network\admin`. No hace falta instalar el cliente de Oracle (usa ODP.NET administrado).
4. **Clave maestra:** se toma de la variable de entorno `SHARED_SERVERS_CLAVE`; si no existe, la app la pide al abrir (3 intentos). Sólo vive en memoria durante la sesión: no se guarda en el archivo ni en el código.
5. Al abrir, la app llama `AccessControl.InicializarClaveMaestra(clave)` y `DiccionarioService.ObtenerCredencialesDeServidor("IFACTJUP")`. Comprueba que el *Data Source* sea `IFACTJUP` (si no, la clave es incorrecta), sustituye `{userX}` / `{passwordX}` y abre la conexión. Si la contraseña vence, vuelve a pedir la cuenta al DLL.

Si algo falla (no está el DLL, la clave es incorrecta, no se encuentra el alias o no existen las tablas), la app muestra el motivo y pregunta si continuar en modo **Local**.

| Operación | Tablas |
|---|---|
| Alta y edición de retornos (validación → permisos → `ROW_VERSION`) | `IB_PROCESS_PALLET_RETURNS`, `IB_PROCESS_FOLIO_COUNTERS`, `IB_PROCESS_STATUS_HISTORY` |
| Bitácora (sólo inserción) | `IB_PROCESS_AUDIT_LOG` |
| Primera ejecución, *My profile*, *Users & Access*, solicitudes | `IB_PROCESS_USERS`, `IB_PROCESS_USER_ACCESS`, `IB_PROCESS_PROFILE_REQUESTS`, `IB_PROCESS_REQUEST_VS` |
| *Import MM60 file* / hoja MM60 del Excel | `IB_PROCESS_MM60_COSTS` (si un precio cambia se cierra la fila anterior con `VALID_TO`) |
| *Mark as handled* | `IB_PROCESS_ALERTS` |
| *Send feedback* | `IB_PROCESS_FEEDBACK` |
| Parámetros de cálculo (0.19, 540 días, umbrales) | `IB_PROCESS_PARAMETERS` (lectura) |
| Catálogos (departamentos, categorías de estatus) | `IB_PROCESS_DEPARTMENTS`, `IB_PROCESS_STATUS_CATEGORIES` (lectura) |
| *Import Excel 2.0* (Owner) | Agrega el histórico **una sola vez** por hoja y renglón (`LEGACY_SHEET`, `LEGACY_ROW`); no reemplaza nada |

Cómo queda "sin borrar":

* Quitar un acceso (**End access**) cierra la fila con `VALID_TO` y `REMOVED_BY`. Cambiar la función cierra la fila anterior y crea una nueva.
* Una solicitud reemplazada queda como `REJECTED` ("Replaced by a newer request").
* Un precio MM60 nuevo cierra el anterior. Los materiales que no vienen en el archivo conservan su último precio.
* *Reset demo data* sólo existe en modo Local.
* Si dos personas editan el mismo retorno, el segundo guardado se rechaza (`ROW_VERSION`) y se pide recargar; nadie sobrescribe a nadie.
* El botón **Reload** vuelve a leer lo que guardaron otras personas.

## Configuración (`appsettings.json`, junto al ejecutable)

Se crea con valores por defecto la primera vez que se abre la app. Si es de una versión anterior, se le agregan las claves nuevas y se conservan sus valores.

```json
{
  "DataSource": "Oracle",
  "Oracle": {
    "Server": "IFACTJUP",
    "SharedServersDll": "SharedServersAccess.dll",
    "SharedServersIni": "",
    "TnsAdmin": "",
    "Schema": "",
    "MasterKeyEnvVar": "SHARED_SERVERS_CLAVE",
    "AuditRowsShown": 5000
  },
  "Owners": [ "US\\RLN8JU" ],
  "UserStorePath": "",
  "DemoData": true,
  "DemoUsers": true
}
```

* **DataSource:** `Oracle` o `Local`.
* **Oracle.SharedServersIni:** ruta opcional de `servers.ini`; vacío = la que trae el DLL.
* **Oracle.Schema:** dueño de las tablas si la cuenta del DLL no es la dueña (se antepone `ESQUEMA.` a cada tabla).

* **Owners:** usuarios de Windows con la función Owner (con o sin dominio). Por defecto `US\RLN8JU`. También puede haber filas OWNER en la tabla de accesos.
* **UserStorePath:** (modo Local) archivo de usuarios. Vacío = `%LOCALAPPDATA%\ExpoReturn\users.json`. Si se apunta a una carpeta compartida, varios equipos ven los mismos usuarios (piloto).
* **DemoData / DemoUsers:** pallets sintéticos (sólo en modo Local) y usuarios ficticios (uno por área y VS) para que el Owner pruebe funciones con "View as". Los usuarios demo sólo viven en memoria, también con Oracle: nunca se guardan.

## Departamentos

Lista vigente: MFQ8, MFO1, MFO12, MFO1-US, MFE5, QMM1, QMM2, LOP1, LOP2, LOW, CTG3, TEF6, GS/OSD43 y HSE. En *Digitalización y proyectos transversales*: QMM-L2, DBE y TEF1. Los códigos de la lista anterior (MFQ4, QMM-C1, PUQ…) quedan **inactivos**: no se ofrecen, pero los registros históricos que los usan siguen siendo válidos. La hoja *Departments* del diccionario muestra el área de permisos de cada uno.

## Qué incluye

| Pantalla | Qué permite revisar |
|---|---|
| **Primera ejecución / My profile** | Lee el usuario de Windows automáticamente y pide nombre, Department y Value Stream(s). Empieza como **Viewer**. Después, cambiar Department o VS queda como **solicitud** para el Owner; el nombre se cambia directo. La función nunca se elige aquí |
| **Barra superior** | Usuario de Windows, department, VS y función. El Owner ve las solicitudes pendientes y un selector **View as** para ver la app con el acceso de otro usuario (aparece el aviso "PREVIEW") |
| **Returns inbox** | Lista filtrable por VS, estatus, texto y riesgo arancelario; indicadores; orden por vencimiento IMMEX; filtros recordados por usuario; exportar a Excel de sólo lectura |
| **Return detail / New return** | Campos del Excel 2.0 con nombres en inglés; al pasar el cursor, columna del Excel, regla y ejemplo. **LOW export number + date** divididos. Asterisco en obligatorios; pendientes agrupados; **Status Category** con la lista de su hoja VS. Campos editables en **verde ligero** según la función (Champion/Proxy) del usuario en el VS del registro |
| **Balance / Duties** | Sustituto de la hoja BALANCE, por VS y año fiscal; clic en un VS o estatus abre la bandeja filtrada |
| **Alerts** | Agrupadas por área responsable; "Mark as handled" con comentario |
| **Users & Access** | Tabla **User / Department / Value Stream / Function** (Viewer, Proxy, Champion, Owner); sólo el Owner la edita. Solicitudes pendientes con **Approve / Reject**. Matriz Champion/Proxy por VS y área, y campos que edita cada área |
| **Audit log** | Quién cambió qué campo, cuándo, valor anterior y nuevo, incluidos accesos y solicitudes; filtros por folio, usuario, campo y fechas |
| **Import MM60 file… (Owner)** | Carga el reporte MM60 exportado de SAP (columnas Material, Price, Price unit; el mismo de la hoja oculta "MM60 COST SAP"). Actualiza el maestro de costos sin borrar materiales, completa el costo de los registros abiertos que se guardaron sin él y los recalcula. Abajo del botón se ve cuántos materiales hay cargados |
| **Import Excel 2.0…** | Local: carga la MATRIZ DE RASTREO en memoria y en sólo lectura. Oracle (sólo Owner): agrega el histórico a la base una sola vez y avisa qué renglones rechazó y por qué. Divide la expo LOW en número y fecha y carga la hoja "MM60 COST SAP" |
| **Reload** (Oracle) | Vuelve a leer lo que guardaron otras personas |
| **Send feedback** | Oracle: `IB_PROCESS_FEEDBACK`. Local: `%LOCALAPPDATA%\ExpoReturn\feedback.csv` |

## Atajos de teclado

| Atajo | Dónde | Acción |
|---|---|---|
| Ctrl+N | Cualquier pantalla | Nuevo retorno |
| Ctrl+F | Bandeja | Ir a la búsqueda |
| Enter | Bandeja | Abrir el renglón seleccionado |
| Ctrl+E | Bandeja | Exportar a Excel (sólo lectura) |
| Ctrl+S | Detalle | Guardar |
| Ctrl+1 … Ctrl+6 | Detalle | Ir a la pestaña 1 a 6 |
| Esc | Detalle | Regresar a la bandeja (pregunta si hay cambios sin guardar) |

## Guion de revisión sugerido (10 minutos)

0. **Oracle:** con el DLL junto al exe, abrir la app, escribir la clave maestra (o definir `SHARED_SERVERS_CLAVE`) y comprobar arriba la leyenda *Oracle IFACTJUP · usuario · 5 value streams*.
1. **Primera ejecución:** abrir la app; aparece *Welcome*. El usuario de Windows ya viene escrito; elegir Department y VS y guardar. Arriba a la derecha aparece como **Viewer**: todo en gris.
2. Abrir con el usuario `US\RLN8JU` (Owner por defecto): aparece como **Owner** y ve *Import MM60 file* y la edición de *Users & Access*. Para otro Owner, agregar su usuario en `"Owners"` de `appsettings.json`.
3. **Users & Access:** agregar una fila con otro usuario (o el suyo con otra configuración) como *Champion* de un VS y guardar; la bitácora registra el cambio.
4. **View as** *Patricia Lozano · GS/OSD43 · VS1 · Champion*: en un registro de VS1 sólo los campos de aduana quedan en verde; en otro VS todo queda en gris.
5. **My profile** con un usuario no Owner: cambiar el VS. Aparece como solicitud; el Owner la aprueba en **Users & Access** y el VS nuevo entra como Viewer.
6. Abrir un registro *Pending export* de VS1 (como Owner o un GS de VS1) y capturar *Customs Export* `PR26-00123`: pasa a *Exported*. Con `scrap marzo` aparece VAL-046.
7. **Import Excel 2.0…** con el archivo real y revisar *LOW Export Number / Date*. Con Oracle (Owner), importarlo dos veces: la segunda debe decir "already loaded".
8. **Reglas:** borrar el texto de un campo con valor y guardar; la app lo rechaza, también como Owner. En *Users & Access*, **End access** cierra el acceso y lo deja en la historia.
9. **Send feedback** en cada pantalla.

## Limitaciones actuales

* La conexión a Oracle **no se pudo probar** contra `IFACTJUP` desde el entorno de desarrollo: las pruebas automáticas cubren las reglas, el manejo de credenciales del DLL y que el código coincida con los scripts SQL. La primera prueba real es abrir la app en la red de planta con el DLL y la clave.
* En modo Local los pallets se pierden al cerrar y cada equipo tiene sus propios usuarios, salvo que `UserStorePath` apunte a una carpeta de red.
* Los usuarios demo (`DEMO\...`) son **ficticios** y sólo existen en memoria.
* La lista de retornos se lee al abrir y con **Reload**; no se actualiza sola mientras otra persona guarda.
* Las alertas se ven en pantalla; el envío por correo o Teams es de la fase de producción.
* **Sin integración con SAP** (fuera de alcance).
* Parámetros provisionales [VALIDATE GS/OSD43]: tasa de aranceles 0.19 y plazo IMMEX de 540 días. Las áreas de permisos de MFE, TEF, CTG, DBE y QMM-L están por confirmar.

## Estructura

| Proyecto | Contenido |
|---|---|
| `ExpoReturn.Domain` | `PalletReturn`, catálogos, `Identifiers` (P/N, HU, lote, factura), `FinancialCalculator`, `DutyRisk`, `StatusResolver`, `PalletValidator`, `PalletEvaluator` |
| `ExpoReturn.Application` | Usuario actual y accesos (`UserContext`, `AccessGrant`, `UserFunction`), `IUserDirectory`, matriz de permisos por área (`FieldPermissions`), `PalletReturnService` (permisos → validación → recálculo → auditoría), `ReportingService` |
| `ExpoReturn.Infrastructure.InMemory` | Repositorio y bitácora en memoria, `UserDirectory` (reglas de usuarios, accesos y solicitudes; guarda cambios incrementales, sin borrar) con `JsonUserStore`, `SampleData` (sintético), `LegacyExcelImporter` (ClosedXML) |
| `ExpoReturn.Infrastructure.Oracle` | `SharedServersCredentials` (carga el DLL en tiempo de ejecución), `OracleDb` (alias TNS, transacciones, errores de los triggers), `OraclePalletRepository`, `OracleAuditTrail`, `OracleUserStore`, `OracleMaterialCostCatalog`, `OracleAlertStore`, `OracleFeedbackStore`, `OracleParameters` (ODP.NET administrado) |
| `ExpoReturn.Desktop` | WPF + MVVM (CommunityToolkit.Mvvm): `AppConfig` (appsettings.json), `ProfileWindow`, `ShellViewModel`, `UsersViewModel`, vistas y `Themes/Theme.xaml` |
| `tests/ExpoReturn.Domain.Tests` | 73 pruebas: no vaciar campos ni borrar, concurrencia, departamentos, credenciales del DLL, código ↔ scripts Oracle, casos golden, invariante de balance, normalizadores, fechas heredadas, usuarios y accesos (registro, Owner, solicitudes, persistencia), permisos e importación del Excel real |
