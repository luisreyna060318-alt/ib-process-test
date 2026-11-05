# Generador del documento Word

Los archivos `docs/*.md` son la **fuente de verdad**. El Word (`entregables/SRS_ExpoReturn_FG_JuP1_vX.Y.docx`) se genera a partir de ellos para que se pueda revisar y comentar.

## Uso

```bash
cd tools/docx
npm install                      # una vez (docx, mermaid, playwright)
npx playwright install chromium  # una vez, si no hay Chromium
npm run all                      # renderiza los diagramas y genera el .docx
```

* `document.config.json`: versión, fecha, estatus, autor, aprobadores, historial de versiones y orden de los capítulos. **Para cada versión nueva, actualiza `version` y `output` y agrega un renglón a `history`.**
* `render_mermaid.js`: convierte cada bloque ```` ```mermaid ```` en `docs/img/mermaid-<hash>.png`. Sólo re-renderiza los diagramas que cambiaron y borra las imágenes que ya no se usan.
* `build_docx.js`: convierte el Markdown en Word (portada, control del documento, índice, encabezado y pie, tablas, código, notas). Los diagramas anchos van en páginas horizontales.

## Flujo de revisión recomendado

1. La jefatura revisa y comenta en el `.docx` (control de cambios o comentarios de Word).
2. Los cambios acordados se pasan a los `.md` correspondientes (y a `database/*.sql` si afectan el modelo).
3. Se incrementa la versión en `document.config.json` y se regenera el Word.

Al abrir el Word, acepta **actualizar campos** para que se llene el índice y los números de página (o en Word: clic derecho sobre el índice → *Actualizar campos*).
