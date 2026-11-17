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

## 3. Aplicación (.NET 8 / WPF) · v0.5.0

Solución `ExpoReturn.sln`. **Interfaz en inglés**, con los campos del Excel 2.0 (la columna "LOW export", que tenía dos datos, se dividió en número y fecha de expo).

* **Oracle obligatorio:** todo se guarda en las tablas `IB_PROCESS_` de `IFACTJUP`; no hay modo local ni datos de demostración. La app se conecta sola al abrir, con la cuenta de **SharedServersAccess.dll** y la clave integrada. **El usuario sólo necesita `ExpoReturn.Desktop.exe`**: el DLL va incrustado en el exe y no hay archivo de configuración (para compilar, el DLL se pone en `lib/`, que no se versiona). Si no puede, reintenta de inmediato, a los 5 s y a los 10 s, y no abre si todo falla. Catálogos, permisos por campo y parámetros se leen de la base.
* **IB PROCESS con varios pallets (v0.5.0):** cada registro es un **IB PROCESS** (encabezado: QMM Responsible, departamento solicitante, solicitante, motivo, detalle, fecha y factura de importación, cruce ELP → JuP1 y **Delivery Number**) con uno o más **pallets** (número de parte, cantidad, costo MM60, total SAP, HU **o** lote, fecha de PO, PO, ejecución MFO, exportación LOW, Customs Export, GR, HSE y Status Category). Cada pallet necesita su PO y su HU o lote; el pallet no se repite dentro del proceso, el PO sí. Al agregar un pallet se copian el PO y la fecha de PO del anterior. Si un PO ya está en otro IB PROCESS, la app pregunta *"Este PO ya se encuentra registrado en otro IB PROCESS desea continuar?"* (Cancelar regresa sin perder nada).
* **Estatus del proceso:** Draft sin pallets; Duty risk si algún pallet lo está; si no, el pallet abierto menos avanzado; con todos cerrados: Closed (duty paid) si alguno lo está, si no Exported si alguno lo está, si no Scrap confirmed.
* **Permisos:** **Owner** todo (también cerrados). **Champion / Proxy** en el VS: en los procesos de su departamento crean, editan la solicitud y los campos de su área y borran; en los demás procesos del VS, los campos de su área; también modifican cerrados. **Member** en el VS: en los procesos que creó, solicita o de los que es QMM Responsible, la solicitud y los campos de su área; los Members de áreas de soporte (OSD, LOP, MFO, LOW, HSE) capturan los campos de su área en cualquier proceso del VS; no modifican cerrados ni borran. **Viewer** (o registro pendiente): consulta su VS.
* **Borrado:** D significa borrar de verdad procesos y pallets (con permiso y motivo); la bitácora guarda una copia completa (`SNAPSHOT`) y sigue siendo sólo de inserción. Un campo con valor se puede cambiar pero no dejar vacío (app y triggers). Quitar un acceso lo cierra con fecha.
* **Usuario real:** al abrir, la app lee el usuario de Windows. La primera vez pide nombre, Department y Value Stream(s); queda como **Viewer** con una solicitud de registro. El Owner la aprueba en la pantalla nueva **Requests** y el usuario pasa a **Member**. Al crear un proceso, si el usuario es de QMM queda como QMM Responsible; si no, como solicitante con su departamento (ambos se pueden cambiar eligiendo entre los usuarios registrados).
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

## 4. Base de datos Oracle (prefijo `IB_PROCESS_`)

**Reinstalación v0.5.0 (borra las tablas de la versión anterior):** con la cuenta dueña del esquema, en este orden:

1. [`00_drop_all.sql`](database/oracle/00_drop_all.sql): desactiva y borra el trigger `IB_PROCESS_NO_DROP` y después borra, una por una, **sólo** las tablas `IB_PROCESS_` que lista por nombre (las 19 de v0.4 y las 2 nuevas, si existieran), con sus datos. Ignora las que no existan. Termina con una consulta que debe devolver *no rows selected*.
2. [`IB_PROCESS_install.sql`](database/oracle/IB_PROCESS_install.sql) (abajo).
3. [`90_verify_install.sql`](database/oracle/90_verify_install.sql): secciones 1 a 4 sin filas.
4. Reemplazar el exe por el de v0.5.0 y, como Owner, **Import Excel 2.0…** con la MATRIZ DE RASTREO.

**Instalación (conectado con la cuenta dueña del esquema, la misma que entrega SharedServersAccess):** ejecutar [`IB_PROCESS_install.sql`](database/oracle/IB_PROCESS_install.sql). En SQL Developer, *Run Script* (F5); en SQL*Plus o SQLcl, `@IB_PROCESS_install.sql`. Se detiene en el primer error. Es la unión, en este orden, de:

1. [`01_create_tables.sql`](database/oracle/01_create_tables.sql): `CREATE TABLE` de las 20 tablas (catálogos, usuarios y acceso, solicitudes, permisos por campo, parámetros, **`IB_PROCESS_PROCESSES`** (encabezado) y **`IB_PROCESS_PALLETS`** (pallets, se borran con su proceso), historial de estatus, alertas, bitácora y comentarios), con sus llaves, índices y reglas `CHECK`.
2. [`02_seed_data.sql`](database/oracle/02_seed_data.sql): VS, departamentos, motivos, estatus, categorías de estatus y su lista por VS, permisos por campo, parámetros, contadores de folio y el primer Owner (`US\RLN8JU`).
3. [`03_comments_and_triggers.sql`](database/oracle/03_comments_and_triggers.sql): comentarios de tablas y columnas (nombre en la app y columna del Excel) y el trigger que impide modificar la bitácora.
4. [`04_integrity_rules.sql`](database/oracle/04_integrity_rules.sql): triggers que rechazan **vaciar un campo con valor**. Ya no hay triggers contra DELETE ni contra DROP (v0.5.0); sólo la bitácora sigue protegida contra cambios y borrado.

Cómo encajan con la app:

* Las reglas `CHECK` de `IB_PROCESS_PROCESSES` / `IB_PROCESS_PALLETS` son **las mismas validaciones que bloquean el guardado en la app** (cada una indica su código VAL-xxx). Los avisos de la app (datos pendientes de un borrador, formato de HU, PO, lote o factura) no son reglas de la base. Así Oracle nunca rechaza algo que la app dejó guardar. El histórico del Excel (`IS_LEGACY = 'Y'`) conserva lo que traía.
* Los textos se miden en **caracteres** (`VARCHAR2(n CHAR)`), igual que la app: un acento o una "Ø" de SAP no hacen que un valor "no quepa".
* Los scripts son ASCII puro y no tienen `&` ni líneas en blanco dentro de las instrucciones (SQL*Plus las interpreta).
* Las pruebas automáticas revisan que los nombres midan como máximo 30 caracteres y que ninguno sea palabra reservada. También revisan que cada llave foránea apunte a una tabla ya creada y del mismo tipo, que la semilla use columnas existentes en el orden correcto, que los permisos sembrados sean los de la app y que cada columna de cada SQL de la app exista con el mismo largo.

**Cambios a una base ya instalada:** a partir de v0.5.0 irán en `database/oracle/changes/`, uno por fecha. El de 2026-11-12 (departamentos) quedó sustituido por la reinstalación y se retiró. Los scripts de cambio registran el usuario con dominio (`US\RLN8JU`), no `OS_USER`.

**Verificar la instalación (en cualquier momento):** [`90_verify_install.sql`](database/oracle/90_verify_install.sql) compara lo que de verdad hay en el esquema (diccionario de Oracle) con lo que definen los scripts: columnas con tipo, largo y nulos; constraints; índices y triggers. Sólo muestra **diferencias**: si las secciones 1 a 4 dicen *no rows selected*, el esquema es idéntico. También muestra las filas por tabla y los totales. Sólo hace `SELECT`.

Los scripts 02, 03, 04, el archivo único y el 90 se regeneran con `python3 tools/database/build_all.py`; el 01 se edita a mano.

Nombres de máximo 30 caracteres (compatibles con cualquier versión de Oracle); requiere Oracle 12c o superior por las columnas `IDENTITY`.

El **diccionario** [`entregables/Diccionario_Campos_ExpoReturn.xlsx`](entregables/Diccionario_Campos_ExpoReturn.xlsx) relaciona cada campo de la app con su columna en Oracle y su columna en cada hoja del Excel. Se genera desde [`tools/dictionary/fields.py`](tools/dictionary/fields.py) con `python3 tools/dictionary/build_field_dictionary.py` (la misma lista alimenta los comentarios de las columnas).

> Los valores marcados **[VALIDATE]** (plazo IMMEX, tasa de aranceles, F4/V1, HU/lote, Date PO) son provisionales hasta que los confirmen QMM y OSD.
