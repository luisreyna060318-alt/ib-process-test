# Borrador de la aplicación (v0.1.6): Expo & Return FG Follow Up

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
| **Bandeja de retornos** | Lista filtrable por VS, estatus, texto y riesgo arancelario; indicadores (abiertos, en riesgo arancelario, Balance GENERAL DLLS); orden por proximidad al vencimiento IMMEX; **filtros recordados por usuario**; **exportar a Excel de sólo lectura**; la columna "Calidad de datos" sólo aparece con datos importados |
| **Detalle / Nuevo retorno** | **Fiel al Excel 2.0:** sólo las columnas del Excel, con su encabezado como nombre del campo (Date PO y PO Number se capturan en la Solicitud). Al pasar el cursor, cada campo explica qué es, la regla y un ejemplo; **asterisco en obligatorios**; los avisos obligatorios se agrupan en "Pendientes para pasar a Solicitado". **COMMENTS** ofrece la lista de su hoja VS, igual que en el Excel. Los campos que el usuario puede editar se resaltan en **verde ligero** y el resto queda en gris. Indicadores, estatus y conciliación se **recalculan mientras se escribe**; las validaciones son navegables |
| **Balance / Aranceles** | Sustituto de la hoja BALANCE: por VS y año fiscal, con barras por VS y por estatus. **Clic en un VS o estatus abre la bandeja filtrada** |
| **Alertas** | Plazo IMMEX por vencer o vencido, faltantes, plazos internos de retrabajo, exportación y HSE, costo pendiente. **Agrupadas por área responsable; "Marcar atendida" con comentario** (queda en la bitácora) |
| **Champions VS** | Matriz Champion/Proxy por VS y área (QMM, MFQ, GS/OSD43, LOP, MFO, LOW, HSE) y los campos que edita cada rol. **La matriz delimita en qué VS edita cada usuario, en todas las áreas**; una persona puede estar en varios VS. El Admin QMM/PE agrega asignaciones y da de baja (sin fechas de vigencia; la bitácora guarda cada alta y baja). Al guardar se actualizan los usuarios simulados y sus permisos |
| **Bitácora** | Quién cambió qué campo, cuándo y con qué valor anterior y nuevo. **Filtros por folio, usuario, campo y fechas** |
| **Importar Excel 2.0…** | Carga la MATRIZ DE RASTREO real **en memoria y en sólo lectura**: muestra los 625 registros con las reglas nuevas y la columna "Calidad de datos" con los problemas detectados. Nada se escribe en el Excel |
| **Enviar comentario** | Guarda la opinión del revisor (pantalla, rol, tipo, texto) en `%LOCALAPPDATA%\ExpoReturnBorrador\comentarios.csv` |

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

1. **Bandeja:** filtrar por "Sólo riesgo arancelario" y abrir un folio. Revisar la razón del riesgo en el encabezado.
2. Cambiar el **usuario simulado** (arriba a la derecha; agrupado por área, hay al menos un usuario por área y por VS) a *Patricia Lozano · GS / OSD43 · VS1*. En un registro de VS1 sólo los campos de aduana quedan en verde; en un registro de otro VS todo queda en gris porque no está asignada ahí en la matriz.
3. Abrir un registro en **Pendiente exportación** de VS1 y capturar en *Customs Export* `PR26-00123`. El estatus pasa a *Exportado* y el balance a 0. Probar con un texto como `scrap marzo`: aparece el error VAL-046 y no cierra.
4. Con *Paola Esquer · Manufactura (MFO) · VS7*, abrir un registro *En retrabajo* de VS7 y capturar *Old Material Delivered to LOW* menor a lo importado y luego *Physical material delivered to LOW*. Aparece el aviso VAL-034 (faltante) y el pallet pasa a *Riesgo arancelario*; QMM lo clasifica en *COMMENTS*.
5. **Nuevo retorno** con el usuario *QMM*. Capturar un P/N sin puntos (`0265957012-24W`): se normaliza solo. Guardar y ver el folio asignado y la bitácora.
6. Con el usuario *Admin*, en **Champions VS** seleccionar a *Andrea Salinas · QMM · VS7*, **Dar de baja** y **Guardar cambios**. En el selector de usuario Andrea queda sólo con VS1 y la bitácora registra la baja.
7. **Importar Excel 2.0…** con el archivo real y revisar Balance / Aranceles y la columna "Calidad de datos".
8. **Enviar comentario** en cada pantalla con lo que falte, sobre o confunda.

## Limitaciones conocidas del borrador

* Sin base de datos ni Web API: los cambios se pierden al cerrar.
* El usuario es **simulado**: la lista sale de la matriz Champions VS (cada persona con su área y todos sus VS), más Admin y Consulta. Los datos demo traen un Champion dedicado por área y por VS (35) y Proxies, algunos en varios VS. En producción el usuario sale de Active Directory y sus VS de la misma matriz.
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
| `tests/ExpoReturn.Domain.Tests` | 41 pruebas: casos golden, invariante de balance, normalizadores, fechas heredadas, permisos e importación del Excel real |
