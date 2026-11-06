#!/usr/bin/env python3
"""
Deriva los archivos Markdown de trabajo (docs/*.md) a partir del documento Word,
que es la FUENTE DE VERDAD del proyecto.

    python3 tools/docx/docx_to_md.py                      # usa el .docx más reciente de entregables/
    python3 tools/docx/docx_to_md.py ruta/al/archivo.docx
    python3 tools/docx/docx_to_md.py entregables/UX_Manual_Pantallas_v0.1.docx --out docs/ux

Qué hace:
  1. Convierte el .docx con pandoc (GitHub Markdown, control de cambios ACEPTADO).
  2. Separa un .md por capítulo (cada Título 1). Omite la portada, el control del documento
     y el índice.
  3. Extrae las imágenes (diagramas) a docs/media/.
  4. Exporta los comentarios de Word a docs/_revision_comentarios.md, con el texto
     comentado y el autor, para atenderlos uno por uno.

Requiere: pandoc >= 2.x en el PATH.
"""
import datetime as dt
import glob
import html
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DOCS = os.path.join(ROOT, "docs")
SKIP = {"control del documento", "contenido", "índice", "indice", "table of contents"}


def latest_docx() -> str:
    files = sorted(glob.glob(os.path.join(ROOT, "entregables", "SRS_*.docx")), key=os.path.getmtime)
    if not files:
        sys.exit("No hay .docx en entregables/. Indique la ruta del Word.")
    return files[-1]


def slug(title: str) -> str:
    t = re.sub(r"^\s*(\d{2})\s*[·.\-]\s*", r"\1_", title)
    t = t.replace("&", "y")
    t = re.sub(r"[()/:,¿?¡!\"']", "", t)
    t = re.sub(r"\s+", "_", t.strip())
    t = (t.replace("á", "a").replace("é", "e").replace("í", "i").replace("ó", "o")
          .replace("ú", "u").replace("ñ", "n").replace("Á", "A").replace("É", "E")
          .replace("Í", "I").replace("Ó", "O").replace("Ú", "U").replace("→", "a"))
    return t[:80]


def target_name(title: str) -> str:
    """Conserva los nombres existentes (00_..., 01_...) para que los diffs sean legibles."""
    m = re.match(r"^\s*(\d{2})\s*[·.\-]", title)
    if m:
        existing = glob.glob(os.path.join(DOCS, f"{m.group(1)}_*.md"))
        if existing:
            return os.path.basename(existing[0])
    if title.strip().lower().startswith("resumen ejecutivo"):
        return "Resumen_Ejecutivo.md"
    return slug(title) + ".md"


def export_comments(docx: str) -> list[str]:
    with zipfile.ZipFile(docx) as z:
        if "word/comments.xml" not in z.namelist():
            return []
        cxml = z.read("word/comments.xml").decode("utf8")
        dxml = z.read("word/document.xml").decode("utf8")
    out = []
    for m in re.finditer(r'<w:comment\b([^>]*)>(.*?)</w:comment>', cxml, re.S):
        attrs, body = m.group(1), m.group(2)
        cid = re.search(r'w:id="(\d+)"', attrs).group(1)
        author = html.unescape((re.search(r'w:author="([^"]*)"', attrs) or [None, "?"])[1])
        date = (re.search(r'w:date="([^"]*)"', attrs) or [None, ""])[1][:10]
        text = html.unescape("".join(re.findall(r"<w:t[^>]*>(.*?)</w:t>", body, re.S)))
        rng = re.search(rf'<w:commentRangeStart w:id="{cid}"/>(.*?)<w:commentRangeEnd w:id="{cid}"/>', dxml, re.S)
        anchor = html.unescape("".join(re.findall(r"<w:t[^>]*>(.*?)</w:t>", rng.group(1), re.S))) if rng else ""
        out.append(f"| {cid} | {author} | {date} | {anchor[:160].replace('|', '/')} | {text.replace('|', '/')} |")
    return out


def main():
    global DOCS
    args = sys.argv[1:]
    if "--out" in args:
        i = args.index("--out")
        DOCS = os.path.abspath(args[i + 1])
        del args[i:i + 2]
        os.makedirs(DOCS, exist_ok=True)
    docx = os.path.abspath(args[0]) if args else latest_docx()
    if shutil.which("pandoc") is None:
        sys.exit("Se requiere pandoc (https://pandoc.org/installing.html).")
    rel = os.path.relpath(docx, ROOT)
    md = subprocess.run(
        ["pandoc", docx, "-t", "gfm", "--wrap=none", "--track-changes=accept", "--extract-media=."],
        cwd=DOCS, check=True, capture_output=True, text=True).stdout

    # pandoc nombra la carpeta "media"; las rutas quedan relativas a docs/
    md = md.replace("](./media/", "](media/")
    # Imágenes con tamaño (HTML) -> sintaxis Markdown simple
    md = re.sub(r'<img src="\./(media/[^"]+)"[^>]*/>', r"![](\1)", md)
    chunks = re.split(r"(?m)^# (.+)$", md)
    stamp = dt.date.today().isoformat()
    written = []
    for i in range(1, len(chunks), 2):
        title, body = chunks[i].strip(), chunks[i + 1]
        if title.lower() in SKIP:
            continue
        name = target_name(title)
        header = (f"<!-- GENERADO desde {rel} el {stamp} con tools/docx/docx_to_md.py. "
                  f"La fuente de verdad es el Word: haga los cambios en el .docx y vuelva a generar. -->\n\n")
        with open(os.path.join(DOCS, name), "w", encoding="utf8") as f:
            f.write(header + f"# {title}\n" + body.rstrip() + "\n")
        written.append(name)

    comments = export_comments(docx)
    with open(os.path.join(DOCS, "_revision_comentarios.md"), "w", encoding="utf8") as f:
        f.write(f"# Comentarios de revisión en {os.path.basename(docx)}\n\n")
        if comments:
            f.write("| # | Autor | Fecha | Texto comentado | Comentario |\n|---|---|---|---|---|\n")
            f.write("\n".join(comments) + "\n")
        else:
            f.write("_El documento no tiene comentarios de Word._\n")

    out_rel = os.path.relpath(DOCS, ROOT)
    print(f"Fuente: {rel}")
    for n in written:
        print(f"  -> {out_rel}/{n}")
    print(f"  -> {out_rel}/_revision_comentarios.md ({len(comments)} comentarios)")


if __name__ == "__main__":
    main()
