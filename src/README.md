# Borrador de la aplicación (v0.1): Expo & Return FG Follow Up

Prototipo **ejecutable** para recoger opiniones reales de los usuarios antes de construir la versión definitiva. Respeta la arquitectura del documento (Sección 01): Domain → Application → Infrastructure → Desktop. **No usa base de datos**: todo vive en memoria y se pierde al cerrar.

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

## Qué incluye

| Pantalla | Qué permite revisar |
|---|---|
| **Bandeja de retornos** | Lista filtrable por VS, estatus, texto y Duty Risk; KPIs (abiertos, balance, exposición); orden por proximidad al vencimiento IMMEX |
| **Detalle / Nuevo retorno** | Las 6 pestañas (Solicitud · Aduana/entrada · Ejecución MFO/LOW · Cierre salida/HSE · Balance y estatus · Auditoría). Los campos se habilitan según el **rol y el VS** del usuario. Indicadores, estatus y conciliación de cantidades se **recalculan mientras se escribe**. Las validaciones son navegables: un clic lleva a la pestaña del campo |
| **Balance / Duties** | Sustituto de la hoja BALANCE: por VS y año fiscal, con barras por VS y por estatus |
| **Alertas** | Plazo IMMEX por vencer o vencido, faltantes, SLA de retrabajo, exportación y HSE, costo pendiente |
| **Champions VS** | Matriz Champion/Proxy por VS y área, y los campos que edita cada rol |
| **Bitácora** | Quién cambió qué campo, cuándo y con qué valor anterior y nuevo |
| **Importar Excel 2.0…** | Carga la MATRIZ DE RASTREO real **en memoria y en sólo lectura**: muestra los 625 registros con las reglas nuevas y la columna "Calidad de datos" con los problemas detectados. Nada se escribe en el Excel |
| **Enviar comentario** | Guarda la opinión del revisor (pantalla, rol, tipo, texto) en `%LOCALAPPDATA%\ExpoReturnBorrador\comentarios.csv` |

## Guion de revisión sugerido (10 minutos)

1. **Bandeja:** filtrar por "Duty Risk" y abrir un folio. Revisar la razón del riesgo en el encabezado.
2. Cambiar el **usuario simulado** (arriba a la derecha) a *Comercio Exterior GS/OSD43*. Sólo se habilitan los campos de aduana; el resto queda en gris.
3. Abrir un registro en **Pendiente exportación** y capturar en *Customs Export* `PR26-00123`. El estatus pasa a *Exportado* y el balance a 0. Probar con un texto como `scrap marzo`: aparece el error VAL-046 y no cierra.
4. Con el usuario *Manufactura (MFO)* de VS7, abrir un registro *En retrabajo* de VS7 y capturar `Old Material` menor a lo importado y luego `Physical delivered`. Aparece VAL-034 (faltante); marcar "Declarar faltante" lleva a Duty Risk.
5. **Nuevo retorno** con el usuario *QMM*. Capturar un P/N sin puntos (`0265957012-24W`): se normaliza solo. Guardar y ver el folio asignado y la bitácora.
6. **Importar Excel 2.0…** con el archivo real y revisar Balance / Duties y la columna "Calidad de datos".
7. **Enviar comentario** en cada pantalla con lo que falte, sobre o confunda.

## Limitaciones conocidas del borrador

* Sin base de datos ni Web API: los cambios se pierden al cerrar.
* El usuario es **simulado** (lista de usuarios demo). En producción sale de Active Directory y de la matriz Champions_VS.
* Los nombres de los datos demo son **ficticios**. Con el Excel importado se usan los Champions reales de la hoja "Champions VS", sólo en memoria.
* El costo MM60 de un P/N nuevo se toma de los registros existentes. En producción vendrá del maestro `cat.Material_Cost_MM60`.
* Las alertas se ven en pantalla; el envío por correo o Teams es de la fase de producción.
* **Sin integración con SAP** (fuera de alcance).
* Parámetros provisionales [VALIDAR GS/OSD43]: tasa de duty 0.19 y plazo IMMEX de 540 días.

## Estructura

| Proyecto | Contenido |
|---|---|
| `ExpoReturn.Domain` | `PalletReturn`, catálogos, `Identifiers` (P/N, HU, lote, factura), `FinancialCalculator`, `DutyRisk`, `StatusResolver`, `PalletValidator`, `PalletEvaluator` |
| `ExpoReturn.Application` | Roles y matriz de permisos por campo (`FieldPermissions`), `PalletReturnService` (permisos → validación → recálculo → auditoría), `ReportingService` (Balance y alertas) |
| `ExpoReturn.Infrastructure.InMemory` | Repositorio y bitácora en memoria, `SampleData` (sintético), `ChampionsDirectory`, `LegacyExcelImporter` (ClosedXML) |
| `ExpoReturn.Desktop` | WPF + MVVM (CommunityToolkit.Mvvm): `ShellViewModel`, vistas y `Themes/Theme.xaml` |
| `tests/ExpoReturn.Domain.Tests` | 32 pruebas: casos golden, invariante de balance, normalizadores, fechas heredadas, permisos e importación del Excel real |
