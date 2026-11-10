# Aplicación (v0.2.3): Expo & Return FG Follow Up

Aplicación de escritorio con **interfaz en inglés**. Respeta la arquitectura del SRS (capítulo 01): Domain → Application → Infrastructure → Desktop. **Todavía sin base de datos**: los pallets viven en memoria y se pierden al cerrar; los usuarios y sus accesos se guardan en un archivo JSON hasta conectar Oracle.

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

## Configuración (`appsettings.json`, junto al ejecutable)

Se crea con valores por defecto la primera vez que se abre la app.

```json
{
  "Owners": [ "US\\RLN8JU" ],
  "UserStorePath": "",
  "DemoData": true,
  "DemoUsers": true
}
```

* **Owners:** usuarios de Windows con la función Owner (con o sin dominio). Por defecto `US\RLN8JU`. También puede haber filas OWNER en la tabla de accesos.
* **UserStorePath:** archivo de usuarios. Vacío = `%LOCALAPPDATA%\ExpoReturn\users.json`. Si se apunta a una carpeta compartida, varios equipos ven los mismos usuarios (piloto).
* **DemoData / DemoUsers:** pallets sintéticos y usuarios ficticios (uno por área y VS) para que el Owner pruebe funciones con "View as". Los usuarios demo nunca se guardan.

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
| **Import MM60 file… (Owner)** | Carga el reporte MM60 exportado de SAP (columnas Material, Price, Price unit; el mismo de la hoja oculta "MM60 COST SAP"). Reemplaza el maestro de costos, completa el costo de los registros abiertos que se guardaron sin él y los recalcula. Abajo del botón se ve cuántos materiales hay cargados y de qué archivo |
| **Import Excel 2.0…** | Carga la MATRIZ DE RASTREO en memoria y en sólo lectura; divide la expo LOW en número y fecha; también carga la hoja "MM60 COST SAP" como maestro de costos |
| **Send feedback** | Guarda el comentario en `%LOCALAPPDATA%\ExpoReturn\feedback.csv` |

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

1. **Primera ejecución:** abrir la app; aparece *Welcome*. El usuario de Windows ya viene escrito; elegir Department y VS y guardar. Arriba a la derecha aparece como **Viewer**: todo en gris.
2. Abrir con el usuario `US\RLN8JU` (Owner por defecto): aparece como **Owner** y ve *Import MM60 file* y la edición de *Users & Access*. Para otro Owner, agregar su usuario en `"Owners"` de `appsettings.json`.
3. **Users & Access:** agregar una fila con otro usuario (o el suyo con otra configuración) como *Champion* de un VS y guardar; la bitácora registra el cambio.
4. **View as** *Patricia Lozano · GS/OSD43 · VS1 · Champion*: en un registro de VS1 sólo los campos de aduana quedan en verde; en otro VS todo queda en gris.
5. **My profile** con un usuario no Owner: cambiar el VS. Aparece como solicitud; el Owner la aprueba en **Users & Access** y el VS nuevo entra como Viewer.
6. Abrir un registro *Pending export* de VS1 (como Owner o un GS de VS1) y capturar *Customs Export* `PR26-00123`: pasa a *Exported*. Con `scrap marzo` aparece VAL-046.
7. **Import Excel 2.0…** con el archivo real y revisar *LOW Export Number / Date*.
8. **Send feedback** en cada pantalla.

## Limitaciones actuales

* Pallets sin base de datos: los cambios se pierden al cerrar. Los usuarios se guardan en el archivo JSON hasta conectar Oracle (scripts en `database/oracle/`).
* Con el archivo en `%LOCALAPPDATA%`, cada equipo tiene sus propios usuarios; para compartirlos antes de Oracle, apuntar `UserStorePath` a una carpeta de red.
* Los usuarios demo (`DEMO\...`) son **ficticios** y sólo existen en memoria.
* El maestro MM60 vive en memoria: hay que cargarlo de nuevo al abrir la app (con *Import MM60 file* o *Import Excel 2.0*) hasta conectar Oracle (`IB_PROCESS_MM60_COSTS`).
* Las alertas se ven en pantalla; el envío por correo o Teams es de la fase de producción.
* **Sin integración con SAP** (fuera de alcance).
* Parámetros provisionales [VALIDATE GS/OSD43]: tasa de aranceles 0.19 y plazo IMMEX de 540 días.

## Estructura

| Proyecto | Contenido |
|---|---|
| `ExpoReturn.Domain` | `PalletReturn`, catálogos, `Identifiers` (P/N, HU, lote, factura), `FinancialCalculator`, `DutyRisk`, `StatusResolver`, `PalletValidator`, `PalletEvaluator` |
| `ExpoReturn.Application` | Usuario actual y accesos (`UserContext`, `AccessGrant`, `UserFunction`), `IUserDirectory`, matriz de permisos por área (`FieldPermissions`), `PalletReturnService` (permisos → validación → recálculo → auditoría), `ReportingService` |
| `ExpoReturn.Infrastructure.InMemory` | Repositorio y bitácora en memoria, `UserDirectory` (usuarios, accesos y solicitudes en JSON), `SampleData` (sintético), `LegacyExcelImporter` (ClosedXML) |
| `ExpoReturn.Desktop` | WPF + MVVM (CommunityToolkit.Mvvm): `AppConfig` (appsettings.json), `ProfileWindow`, `ShellViewModel`, `UsersViewModel`, vistas y `Themes/Theme.xaml` |
| `tests/ExpoReturn.Domain.Tests` | 44 pruebas: casos golden, invariante de balance, normalizadores, fechas heredadas, usuarios y accesos (registro, Owner, solicitudes, persistencia), permisos e importación del Excel real |
