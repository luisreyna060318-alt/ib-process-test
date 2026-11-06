<!-- GENERADO desde entregables/UX_Manual_Pantallas_ExpoReturn_v0.1.docx el 2026-11-06 con tools/docx/docx_to_md.py. La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->

# 06 · Control de UX


## Rondas de revisión

| **Ronda** | **Fecha**   | **Versión revisada** | **Material** | **Resultado**                                         | **Versión corregida** |
|-----------|-------------|----------------------|--------------|-------------------------------------------------------|-----------------------|
| 1         | 06-nov-2026 | v0.1                 | 12 capturas  | 8 hallazgos (contraste y presentación)                | v0.1.1                |
| 2         | 06-nov-2026 | v0.1.1               | 12 capturas  | Contraste verificado; 4 hallazgos nuevos              | v0.1.2                |
| 3         | 06-nov-2026 | v0.1.2               | 12 capturas  | **Sin hallazgos nuevos**; todo lo anterior verificado | —                     |

## Registro de hallazgos

Severidad: **Alta** impide leer o usar la función · **Media** confunde o induce a error · **Baja** detalle de presentación.

| **Código** | **Ronda** | **Pantalla**          | **Hallazgo**                                                                                                           | **Sev.** | **Solución aplicada**                                                                               | **Versión** | **Estado** |
|------------|-----------|-----------------------|------------------------------------------------------------------------------------------------------------------------|----------|-----------------------------------------------------------------------------------------------------|-------------|------------|
| UX-001     | 1         | Todas las tablas      | Encabezados de tabla ilegibles: texto oscuro sobre azul oscuro                                                         | Alta     | Se quitó el estilo global de texto que forzaba el color oscuro; ahora el texto es blanco (11.6 : 1) | v0.1.1      | Verificado |
| UX-002     | 1         | PNT-00, 01, 02, 07    | Texto de los botones principales (Guardar, Abrir detalle, Enviar / Guardar comentario) casi invisible                  | Alta     | Misma causa y solución que UX-001                                                                   | v0.1.1      | Verificado |
| UX-003     | 1         | PNT-01, 02            | Etiquetas de estatus con bajo contraste: Borrador 2.5 : 1, Pendiente HSE 3.2, Pendiente exportación 3.6, Exportado 3.3 | Media    | Colores más oscuros (≥ 4.76 : 1); tabla en el capítulo 03                                           | v0.1.1      | Verificado |
| UX-004     | 1         | PNT-02, 04            | Avisos ámbar (VAL-xxx · Advertencia, chip AVISO) con 3.0 : 1                                                           | Media    | Ámbar oscuro `#B45309` (5.0 : 1)                                                                    | v0.1.1      | Verificado |
| UX-005     | 1         | PNT-02, 03            | Texto verde ("Exportado con documento", "Conciliado USD") con 3.3 : 1                                                  | Baja     | Verde `#15803D` (5.0 : 1)                                                                           | v0.1.1      | Verificado |
| UX-006     | 1         | PNT-07                | El botón "Guardar comentario" aparecía recortado                                                                       | Media    | La ventana se ajusta a su contenido                                                                 | v0.1.1      | Verificado |
| UX-007     | 1         | PNT-02 (pestañas 2-4) | Las fechas deshabilitadas se veían blancas, como editables                                                             | Media    | Fondo gris igual que los demás campos bloqueados                                                    | v0.1.1      | Verificado |
| UX-008     | 1         | PNT-02 (pestaña 3)    | Las casillas "cono verde" y "plataforma" mostraban un cuadro relleno (indeterminado) que parecía marcado               | Media    | Casillas sí/no                                                                                      | v0.1.1      | Verificado |
| UX-009     | 2         | PNT-01                | Se perdía el orden por defecto (abiertos más próximos a vencer) después de *Restaurar datos demo*                      | Media    | El orden se aplica a los datos mismos                                                               | v0.1.2      | Verificado |
| UX-010     | 2         | PNT-01                | Columnas recortadas ("Días en JuF", "Días p/venc", categoría "…JuP1")                                                  | Baja     | Columnas más anchas                                                                                 | v0.1.2      | Verificado |
| UX-011     | 2         | PNT-04                | Redacción "Faltan 1 pzs"                                                                                               | Baja     | Singular y plural ("Falta 1 pz")                                                                    | v0.1.2      | Verificado |
| UX-012     | 2         | PNT-05                | El rol "Consulta:" aparecía sin texto                                                                                  | Baja     | "(sólo lectura, no edita campos)"                                                                   | v0.1.2      | Verificado |

## Pendientes de UX (por validar con usuarios)

Mejoras propuestas a partir de las revisiones. Se deciden con los usuarios en la siguiente ronda: no se han implementado.

| **Código** | **Pantalla** | **Observación**                                                                                      | **Propuesta**                                                                                               | **Prioridad** |
|------------|--------------|------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------|---------------|
| UX-B01     | PNT-02       | En un retorno nuevo, el panel muestra 8 avisos antes de capturar nada; puede percibirse como "ruido" | Agrupar como "Pendientes para Solicitado (8)", plegable, o mostrarlos después del primer intento de guardar | Media         |
| UX-B02     | PNT-02       | El folio "VS1-00000" en el alta parece un folio real                                                 | Mostrar "Se asignará al guardar" hasta el primer guardado                                                   | Baja          |
| UX-B03     | PNT-02       | Las etiquetas mezclan el inglés del Excel (Returned Pallet 13 Digits Part Number) con el español     | Decidir el idioma: etiqueta en español con el nombre del Excel como ayuda, o al revés                       | Media         |
| UX-B04     | PNT-02       | No hay marca visual de campos obligatorios                                                           | Asterisco o etiqueta "obligatorio para Solicitado"                                                          | Media         |
| UX-B05     | Todas        | Faltan ayudas contextuales sobre las reglas                                                          | *Tooltips* con la regla VAL-xxx y un ejemplo en cada campo                                                  | Media         |
| UX-B06     | PNT-01       | No se puede exportar la vista ni guardar filtros favoritos                                           | Exportar a Excel (sólo lectura) y recordar los últimos filtros por usuario                                  | Media         |
| UX-B07     | PNT-01       | La columna "Calidad de datos" queda vacía con datos demo                                             | Mostrarla sólo cuando haya registros históricos                                                             | Baja          |
| UX-B08     | PNT-06       | La bitácora no tiene filtros                                                                         | Filtrar por folio, usuario, campo y rango de fechas                                                         | Media         |
| UX-B09     | PNT-04       | Las alertas no se agrupan ni se marcan como atendidas                                                | Agrupar por área responsable; botón "Atendida" con comentario                                               | Media         |
| UX-B10     | PNT-05       | La matriz es de sólo consulta                                                                        | Edición por Admin QMM/PE, con vigencia (producción)                                                         | Baja (fase 2) |
| UX-B11     | Todas        | Diseñada y probada a 1920 × 1080                                                                     | Probar a 1366 × 768 (laptops de piso); mínimo soportado 1150 × 680                                          | Alta          |
| UX-B12     | Todas        | Uso con teclado no revisado                                                                          | Orden de tabulación por pestaña, atajos (Ctrl+S guardar, Ctrl+F buscar)                                     | Media         |
| UX-B13     | PNT-03       | El tablero no lleva al detalle                                                                       | Clic en un VS o estatus → Bandeja filtrada                                                                  | Media         |

## Lista de verificación por ronda

Se aplica a cada pantalla en cada ronda. ✔ cumple · ✖ hallazgo registrado · — no aplica.

| **Criterio (heurísticas de usabilidad)**                                    | **PNT-01** | **PNT-02** | **PNT-03** | **PNT-04** | **PNT-05** | **PNT-06** | **PNT-07** |
|-----------------------------------------------------------------------------|------------|------------|------------|------------|------------|------------|------------|
| Estado visible del sistema (estatus, cambios sin guardar, origen de datos)  | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          |
| Lenguaje del usuario (términos del proceso, no técnicos)                    | ✔          | ✖ UX-B03   | ✔          | ✔          | ✔          | ✔          | ✔          |
| Control y libertad (descartar, regresar, confirmar antes de perder cambios) | ✔          | ✔          | —          | ✔          | —          | —          | ✔          |
| Consistencia (colores, ubicación de botones, formatos)                      | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          |
| Prevención de errores (validación en vivo, campos bloqueados)               | ✔          | ✔          | —          | —          | —          | —          | ✔          |
| Reconocer antes que recordar (catálogos, ayudas)                            | ✔          | ✖ UX-B05   | ✔          | ✔          | ✔          | ✔          | ✔          |
| Flexibilidad (filtros, atajos)                                              | ✖ UX-B06   | ✖ UX-B12   | ✖ UX-B13   | ✖ UX-B09   | —          | ✖ UX-B08   | ✔          |
| Diseño minimalista (sólo lo necesario)                                      | ✖ UX-B07   | ✖ UX-B01   | ✔          | ✔          | ✔          | ✔          | ✔          |
| Mensajes de error claros y accionables                                      | —          | ✔          | —          | ✔          | —          | —          | ✔          |
| Contraste ≥ 4.5 : 1 en texto                                                | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          | ✔          |

## Plantilla de sesión de prueba con usuarios

Una sesión de 30 minutos por rol. El facilitador no ayuda; anota tiempo, errores y comentarios. Al final, el participante deja sus comentarios con **Enviar comentario** (PNT-07).

| **\#** | **Tarea (rol)**                                            | **Criterio de éxito**                  | **Tiempo objetivo** | **Tiempo real** | **Errores** | **Comentarios** |
|--------|------------------------------------------------------------|----------------------------------------|---------------------|-----------------|-------------|-----------------|
| T1     | Encontrar los pallets en Duty Risk de su VS (todos)        | Usa el filtro de VS y "Sólo Duty Risk" | 1 min               |                 |             |                 |
| T2     | Crear un retorno con un P/N sin puntos y guardarlo (QMM)   | Folio asignado; estatus Solicitado     | 3 min               |                 |             |                 |
| T3     | Registrar el cruce ELP-JuP1 de un folio Solicitado (GS)    | Estatus Importado                      | 2 min               |                 |             |                 |
| T4     | Cerrar el retrabajo con una pieza faltante (MFO)           | Entiende VAL-034 y declara el faltante | 3 min               |                 |             |                 |
| T5     | Exportar un pallet pendiente con PRyy-##### (GS)           | Estatus Exportado; balance 0           | 2 min               |                 |             |                 |
| T6     | Confirmar un scrap (HSE)                                   | Scrap sale del balance                 | 2 min               |                 |             |                 |
| T7     | Responder "¿cuánto duty está en riesgo en VS7?" (jefatura) | Lo lee en Balance / Duties             | 1 min               |                 |             |                 |
| T8     | Explicar por qué un campo está en gris (cualquiera)        | Menciona rol / VS / etapa              | 1 min               |                 |             |                 |
