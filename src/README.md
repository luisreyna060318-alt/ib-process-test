# Aplicación (v0.4.2): Expo & Return FG Follow Up

Aplicación de escritorio con **interfaz en inglés**. Respeta la arquitectura del SRS (capítulo 01): Domain → Application → Infrastructure → Desktop.

**Siempre trabaja sobre la base Oracle** (tablas `IB_PROCESS_` de `IFACTJUP`): no existe modo local ni datos de demostración, así que no hay confusión sobre si un cambio se guardó. Al abrir, la app se conecta sola, con la cuenta que entrega **SharedServersAccess.dll** (igual que el worker de good-receipt). Si no logra conectarse, no abre.

Los catálogos también se leen de la base: Value Streams, motivos, categorías de estatus y su lista por VS, departamentos, nombres de estatus, permisos por campo y parámetros. Se administran ahí, sin publicar un exe nuevo.

**Reglas de datos (también para el Owner):** nunca se borra nada. Un campo que ya tiene valor se puede modificar, pero no dejar vacío. Lo cumplen la app y la base de datos (triggers de `04_integrity_rules.sql`).

## Cómo abrirlo

**Opción A · usuarios:** sólo se necesita `ExpoReturn.Desktop.exe`, la versión autocontenida para Windows 10/11 x64. No requiere .NET, ni archivo de configuración, ni copiar el DLL: SharedServersAccess.dll viaja dentro del exe.

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
2. **SharedServersAccess.dll va dentro del exe** (recurso incrustado, se carga desde memoria); el usuario no lo copia. Para compilar, poner el DLL en `lib/SharedServersAccess.dll`; esa carpeta no se versiona. Si al compilar no está, la app lo busca junto al exe.
3. `tnsnames.ora`: la app resuelve el alias `IFACTJUP` como el worker de Python, agregando `NAMES.DEFAULT_DOMAIN` de `sqlnet.ora`. Busca la carpeta en la variable `TNS_ADMIN` y luego en `%ORACLE_HOME%\network\admin`. No hace falta instalar el cliente de Oracle (usa ODP.NET administrado).
4. **Clave maestra:** está en el código; la conexión es automática y nadie la escribe.
5. Al abrir, la app llama `AccessControl.InicializarClaveMaestra(clave)` y `DiccionarioService.ObtenerCredencialesDeServidor("IFACTJUP")`. Comprueba que el *Data Source* sea `IFACTJUP` (si no, la clave es incorrecta), sustituye `{userX}` / `{passwordX}` y abre la conexión. Si la contraseña vence, vuelve a pedir la cuenta al DLL.

**Si no se puede conectar** (red, `servers.ini` de la carpeta de red, alias de tnsnames, tablas), aparece una ventana *"Could not connect to the database"* con el motivo, y la app reintenta tres veces: **de inmediato, a los 5 segundos y a los 10 segundos**, con cuenta regresiva. Si se conecta, la ventana se cierra y la app abre normalmente. Si los tres reintentos fallan, muestra qué revisar y **la app no abre**. Cerrar la ventana detiene los reintentos.

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
| *Import Excel 2.0* (Owner) | Guarda cada renglón del Excel como **registro de trabajo editable**, una sola vez por hoja y renglón (`LEGACY_SHEET`, `LEGACY_ROW`). Una segunda importación nunca sobrescribe lo que ya se corrigió. La hoja MM60 COST SAP va a `IB_PROCESS_MM60_COSTS` y completa el costo de los registros que no lo tenían |

Cómo queda "sin borrar":

* Quitar un acceso (**End access**) cierra la fila con `VALID_TO` y `REMOVED_BY`. Cambiar la función cierra la fila anterior y crea una nueva.
* Una solicitud reemplazada queda como `REJECTED` ("Replaced by a newer request").
* Un precio MM60 nuevo cierra el anterior. Los materiales que no vienen en el archivo conservan su último precio.
* Si dos personas editan el mismo retorno, el segundo guardado se rechaza (`ROW_VERSION`) y se pide recargar; nadie sobrescribe a nadie.
* El botón **Reload** vuelve a leer lo que guardaron otras personas.

## Configuración (en el código, sin archivos)

No hay `appsettings.json`: si quedó uno de una versión anterior junto al exe, se puede borrar; la app ya no lo lee. Lo fijo está en el código y lo que cambia se administra en la base de datos:

| Qué | Dónde |
|---|---|
| Servidor `IFACTJUP`, clave maestra, SharedServersAccess.dll | Código (`OracleSettings`), DLL incrustado en el exe |
| `tnsnames.ora` | Variable `TNS_ADMIN` o `%ORACLE_HOME%\network\admin` del equipo |
| Owner inicial `US\RLN8JU` | Código (`AppConfig`) |
| Otros Owners, Champions, Proxies | `IB_PROCESS_USER_ACCESS` (pantalla *Users & Access*) |
| Departamentos, VS, motivos, categorías, nombres de estatus, permisos por campo | Tablas `IB_PROCESS_` de catálogos |
| Tasa de aranceles, plazo IMMEX, umbrales | `IB_PROCESS_PARAMETERS` |

Lo único que la app guarda en el equipo son los filtros recordados de cada usuario (`%LOCALAPPDATA%\ExpoReturn\preferences.json`); si no existe, se crea solo.

## Departamentos

**Desde la app (sólo Owner):** en *Users & Access*, el botón **Departments…** abre la ventana para dar de alta un departamento (código, área, grupo, si puede solicitar) y para activar o desactivar los existentes. Cada cambio queda en la bitácora. La pantalla *Users & Access* sólo aparece en el menú del Owner.

**Con Oracle, los departamentos se administran en la base** (`IB_PROCESS_DEPARTMENTS`); no hace falta un exe nuevo. La app los lee al abrir y con **Reload**:

* `IS_ACTIVE = 'Y'`: se ofrece en *My profile* y en *Users & Access*.
* Además `CAN_REQUEST = 'Y'`: se ofrece en *Requesting Department* de un retorno.
* `AREA` decide qué campos editan sus Champions / Proxies (QMM, MFQ/MFE/TEF/PUQ = solicitante, GS, LOP, MFO, LOW, HSE; CTG, DBE y QMM-L sólo consultan).
* `DEPARTMENT_GROUP = 'CROSS_FUNCTIONAL'`: aparece marcado como *digitalization / cross-functional*.
* Nunca se borra un departamento (los registros que lo usan siguen siendo válidos): se desactiva con `IS_ACTIVE = 'N'`.

```sql
-- agregar uno
INSERT INTO IB_PROCESS_DEPARTMENTS (DEPARTMENT_CODE, AREA, CAN_REQUEST, DEPARTMENT_GROUP, IS_ACTIVE)
VALUES ('MFQ3', 'MFQ', 'Y', 'PROCESS', 'Y');
-- dejar de ofrecerlo
UPDATE IB_PROCESS_DEPARTMENTS SET IS_ACTIVE = 'N' WHERE DEPARTMENT_CODE = 'MFQ3';
COMMIT;
```

La lista interna de la app (igual a la que siembra `02_seed_data.sql`) sólo se usa en las pruebas automáticas.


Lista vigente: MFQ8, MFO1, MFO12, MFO1-US, MFE5, QMM1, QMM2, LOP1, LOP2, LOW, CTG3, TEF6, GS/OSD43 y HSE; los departamentos solicitantes de la lista del Excel 2.0 (hoja *Sheet1*): MFQ1, MFQ4, MFQ5, MFQ7, MFQ9, QMM-C1, QMM-C2 y PUQ; y LOW2, que aparece en renglones del Excel. En *Digitalización y proyectos transversales*: QMM-L2, DBE y TEF1. Los demás códigos antiguos (QMM, MFQ, MFQ2, MFQ3, MFQ6, MFO, MFO4, MFO5, MFO7, MFO9, LOP) quedan **inactivos**: no se ofrecen, pero los registros que los usan siguen siendo válidos. Los cambios a una base ya instalada están en `database/oracle/changes/` (por fecha).

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
| **Import Excel 2.0…** (Owner) | Guarda la MATRIZ DE RASTREO en la base como registros **editables** para homogeneizar los datos. Divide la expo LOW en número y fecha, guarda la hoja "MM60 COST SAP" como maestro de costos, completa los costos faltantes y avisa qué renglones rechazó y por qué |
| **Reload** (Oracle) | Vuelve a leer lo que guardaron otras personas |
| **Send feedback** | Se guarda en `IB_PROCESS_FEEDBACK` |

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

0. **Conexión:** abrir el exe (no necesita nada junto a él): se conecta sola y arriba aparece *Oracle IFACTJUP · usuario · 5 value streams*.
1. **Primera ejecución:** abrir la app; aparece *Welcome*. El usuario de Windows ya viene escrito; elegir Department y VS y guardar. Arriba a la derecha aparece como **Viewer**: todo en gris.
2. Abrir con el usuario `US\RLN8JU` (Owner por defecto): aparece como **Owner** y ve *Import MM60 file* y la edición de *Users & Access*. Para otro Owner, darle la función *Owner* en *Users & Access*.
3. **Users & Access:** agregar una fila con otro usuario (o el suyo con otra configuración) como *Champion* de un VS y guardar; la bitácora registra el cambio.
4. **View as** *Patricia Lozano · GS/OSD43 · VS1 · Champion*: en un registro de VS1 sólo los campos de aduana quedan en verde; en otro VS todo queda en gris.
5. **My profile** con un usuario no Owner: cambiar el VS. Aparece como solicitud; el Owner la aprueba en **Users & Access** y el VS nuevo entra como Viewer.
6. Abrir un registro *Pending export* de VS1 (como Owner o un GS de VS1) y capturar *Customs Export* `PR26-00123`: pasa a *Exported*. Con `scrap marzo` aparece VAL-046.
7. **Import Excel 2.0…** con el archivo real y revisar *LOW Export Number / Date*. Con Oracle (Owner), importarlo dos veces: la segunda debe decir "already loaded". Abrir un registro importado, corregir un dato y guardar; con **To homogenize** en la bandeja se ven los que todavía rompen alguna regla.
8. **Reglas:** borrar el texto de un campo con valor y guardar; la app lo rechaza, también como Owner. En *Users & Access*, **End access** cierra el acceso y lo deja en la historia.
9. **Send feedback** en cada pantalla.

## Limitaciones actuales

* La conexión a Oracle **no se pudo probar** contra `IFACTJUP` desde el entorno de desarrollo: las pruebas automáticas cubren las reglas, el manejo de credenciales del DLL y que el código coincida con los scripts SQL. La primera prueba real es abrir la app en la red de planta con el DLL y la clave.
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
| `ExpoReturn.Infrastructure.Oracle` | `SharedServersCredentials` (DLL incrustado, cargado desde memoria), `OracleCatalogs` (catálogos de la base), `OracleDb` (alias TNS, transacciones, errores de los triggers), `OraclePalletRepository`, `OracleAuditTrail`, `OracleUserStore`, `OracleMaterialCostCatalog`, `OracleAlertStore`, `OracleFeedbackStore`, `OracleParameters` (ODP.NET administrado) |
| `ExpoReturn.Desktop` | WPF + MVVM (CommunityToolkit.Mvvm): `AppConfig` (valores fijos), `ConnectionWindow` (reintentos), `ProfileWindow`, `ShellViewModel`, `UsersViewModel`, vistas y `Themes/Theme.xaml` |
| `tests/ExpoReturn.Domain.Tests` | 101 pruebas: reintentos de conexión, catálogos de la base, DLL incrustado, no vaciar campos ni borrar, concurrencia, departamentos, credenciales del DLL, código ↔ scripts Oracle, casos golden, invariante de balance, normalizadores, fechas heredadas, usuarios y accesos (registro, Owner, solicitudes, persistencia), permisos e importación del Excel real |
