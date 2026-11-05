# Documento del proyecto: flujo de trabajo

## Fuente de verdad: el Word

**El documento Word es la fuente de verdad** (`entregables/SRS_ExpoReturn_FG_JuP1_vX.Y.docx`). La jefatura y las áreas revisan y comentan ahí, con comentarios o control de cambios.

Los archivos `docs/*.md` son una **copia de trabajo derivada** del Word. Sirven para leer, buscar, comparar versiones (diff) y que Claude y tú trabajen sobre el texto. Cada uno lleva un encabezado `GENERADO desde …`. **No se editan a mano**: se regeneran.

```
             revisión (Word)                    docx_to_md.py
 jefatura ─────────────────► SRS_vX.Y.docx ──────────────────► docs/*.md  +  docs/_revision_comentarios.md
                                   ▲                                   │
                                   │  nueva versión (vX.Y+1)            │  lectura / diff / trabajo con Claude
                                   └───────────────────────────────────┘
```

## Comandos

```bash
# 1) Derivar los .md desde el Word más reciente de entregables/ (o desde la ruta indicada)
python3 tools/docx/docx_to_md.py
python3 tools/docx/docx_to_md.py entregables/SRS_ExpoReturn_FG_JuP1_v0.3.docx

# 2) Renderizar diagramas (fuentes en docs/diagrams/*.mmd -> .png)
cd tools/docx && npm install && npx playwright install chromium && npm run diagrams
```

`docx_to_md.py` requiere **pandoc**. Hace lo siguiente:

* acepta el control de cambios del Word;
* separa un `.md` por capítulo (Título 1) y omite la portada, el control del documento y el índice;
* extrae las imágenes a `docs/media/`;
* exporta los **comentarios** de Word a `docs/_revision_comentarios.md`, con autor, fecha, texto comentado y comentario, para atenderlos uno por uno.

## Cómo se hace una nueva versión

1. **Cambios de la jefatura o las áreas:** se hacen en Word sobre la versión vigente y se guardan como `vX.Y+1`. Después se ejecuta `docx_to_md.py` y se hace commit del `.docx` y de los `.md` regenerados.
2. **Cambios que propone Claude:** Claude edita el Word directamente (cambios puntuales) o, cuando son cambios amplios y **no hay comentarios pendientes**, edita los `.md` y regenera el Word con `build_docx.js`. Luego vuelve a derivar los `.md`. El ida y vuelta Word → Markdown → Word conserva el texto, las tablas, el código, las notas y las imágenes; los comentarios y el control de cambios de Word **no** se conservan, por eso primero se atienden.
3. Se actualiza `document.config.json` (versión, fecha, historial), que alimenta la portada y el control del documento.

## Archivos

| Archivo | Uso |
|---|---|
| `docx_to_md.py` | Word → `docs/*.md` (flujo normal) |
| `build_docx.js` | `docs/*.md` → Word con formato corporativo: portada, control del documento, índice, páginas horizontales para diagramas anchos. `DOCX_OUTPUT=ruta.docx` cambia el destino |
| `render_mermaid.js` | Diagramas `docs/diagrams/*.mmd` → `.png` |
| `document.config.json` | Versión, fecha, estatus, autor, aprobadores, historial y orden de capítulos |

Al abrir el Word, acepta **actualizar campos** para que se llene el índice (o en Word: clic derecho sobre el índice → *Actualizar campos*).
