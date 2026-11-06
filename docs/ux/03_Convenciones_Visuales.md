<!-- GENERADO desde entregables/UX_Manual_Pantallas_ExpoReturn_v0.1.docx el 2026-11-06 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# 03 · Convenciones visuales


## Colores de estatus

El estatus de cada pallet se muestra como una **etiqueta de color** con texto blanco. El estatus **no se captura**: lo calcula el sistema a partir de los datos (SRS §03, máquina de estados). El contraste mínimo aceptado es **4.5 : 1** (nivel AA de accesibilidad para texto normal).

| **Estatus**           | **Color**                | **Contraste con texto blanco** | **Significado para el usuario**                                      |
|-----------------------|--------------------------|--------------------------------|----------------------------------------------------------------------|
| Borrador              | Gris oscuro `#4B5563`    | 7.56                           | Faltan datos de la solicitud (Etapa 1)                               |
| Solicitado            | Gris azulado `#64748B`   | 4.76                           | Solicitud completa; aún no cruza a JuP1                              |
| Importado             | Azul `#2E75B6`           | 4.84                           | Ya cruzó El Paso → JuP1; inicia el plazo IMMEX                       |
| En retrabajo          | Índigo `#4F46E5`         | 6.29                           | MFO trabaja el material                                              |
| Pendiente HSE         | Ámbar oscuro `#B45309`   | 5.02                           | Hay scrap sin confirmación oficial de HSE                            |
| Pendiente exportación | Naranja oscuro `#C2410C` | 5.18                           | Material en LOW sin pedimento de exportación                         |
| Duty Risk             | Rojo `#DC2626`           | 4.83                           | Riesgo de pagar aranceles: plazo por vencer, faltante o discrepancia |
| Exportado             | Verde `#15803D`          | 5.02                           | Cerrado: todo exportado (y el scrap confirmado)                      |
| Scrap confirmado      | Verde azulado `#0F766E`  | 5.47                           | Cerrado: 100 % destruido con confirmación de HSE                     |
| Cerrado c/duty pagado | Morado `#7C3AED`         | 5.70                           | Cerrado con discrepancia y duty pagado (o cierre F4)                 |
| Cancelado             | Gris `#6B7280`           | 4.83                           | No se importó                                                        |

## Severidad de validaciones y alertas

| **Severidad**       | **Color**              | **Contraste** | **Efecto**                                                           |
|---------------------|------------------------|---------------|----------------------------------------------------------------------|
| Error / CRÍTICA     | Rojo oscuro `#B91C1C`  | 6.47          | **Impide guardar** (validaciones) o exige acción inmediata (alertas) |
| Advertencia / AVISO | Ámbar oscuro `#B45309` | 5.02          | Permite guardar, pero queda señalado                                 |
| Información         | Azul `#2E75B6`         | 4.84          | Dato informativo                                                     |

## Campos editables y bloqueados

| **Aspecto**                        | **Significado**                                                                                    |
|------------------------------------|----------------------------------------------------------------------------------------------------|
| Campo con **fondo blanco**         | El usuario actual puede editarlo                                                                   |
| Campo con **fondo gris**           | Pertenece a otra área o a otra etapa, o el registro está cerrado. Se ve, pero no se edita          |
| Leyenda bajo el título del detalle | Dice cuántos campos puede editar el usuario o por qué el registro es de sólo lectura               |
| Texto gris pequeño bajo un campo   | Ayuda o regla breve (p. ej. "Consecutivo por VS, se asigna al guardar")                            |
| Texto azul bajo un campo           | Resultado automático (p. ej. "Se guardará como 0203.306.644-250" o el tipo de documento de cierre) |

## Tipografía y formatos

| **Elemento**              | **Convención**                                               |
|---------------------------|--------------------------------------------------------------|
| Fuente                    | Segoe UI (la de Windows)                                     |
| Títulos de pantalla       | 22 pt, azul oscuro `#1F3864`                                 |
| Texto principal           | Gris carbón `#1F2937` (contraste 14.7 : 1)                   |
| Texto secundario / ayudas | Gris `#6B7280` (4.83 : 1)                                    |
| Encabezados de tabla      | Texto blanco sobre azul oscuro (11.6 : 1)                    |
| Fechas                    | dd/mm/aaaa (formato México), con selector de calendario      |
| Importes                  | USD con separador de miles y 2 decimales (p. ej. 43,197.26)  |
| Número de parte           | Formato SAP `XXXX.XXX.XXX-XX(X)`; se normaliza al capturarlo |
| Folio                     | `VSn-#####` (p. ej. VS7-00012), consecutivo por Value Stream |

## Mensajes

| **Tipo**                 | **Dónde**                                         | **Ejemplo**                                       |
|--------------------------|---------------------------------------------------|---------------------------------------------------|
| Confirmación de guardado | Barra inferior del detalle, fondo verde claro     | "Guardado. Folio VS7-00012 · Estatus: Importado." |
| Error al guardar         | Barra inferior del detalle, fondo rojo claro      | "No se guardó: 2 error(es) de validación."        |
| Permiso denegado         | Barra inferior del detalle                        | "Sin permiso para modificar: ImportInvoice"       |
| Cambios sin guardar      | Etiqueta roja junto al título + pregunta al salir | "Hay cambios sin guardar. ¿Descartarlos?"         |
