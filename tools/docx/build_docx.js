#!/usr/bin/env node
/*
 * Genera el documento Word del proyecto a partir de los capítulos Markdown
 * (fuente de verdad) listados en document.config.json.
 *
 *   1) node tools/docx/render_mermaid.js   (sólo si cambió algún diagrama)
 *   2) node tools/docx/build_docx.js
 *
 * Markdown soportado: #..####, párrafos, **negrita**, *cursiva*, `código`,
 * [enlaces](...), listas (-, *, 1.), tablas con |, > notas, ``` código,
 * ```mermaid (imagen pre-renderizada en docs/img), ---.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell,
  Header, Footer, AlignmentType, HeadingLevel, LevelFormat, BorderStyle, WidthType,
  ShadingType, PageNumber, PageBreak, TableOfContents, PositionalTab,
  PositionalTabAlignment, PositionalTabRelativeTo, PositionalTabLeader, VerticalAlign,
  TableLayoutType, PageOrientation,
} = require('docx');

const ROOT = path.resolve(__dirname, '..', '..');
const cfg = JSON.parse(fs.readFileSync(path.join(__dirname, 'document.config.json'), 'utf8'));

/* ---------- Diseño ---------- */
const FONT = 'Arial';
const MONO = 'Consolas';
const C = { primary: '1F3864', accent: '2E75B6', text: '222222', muted: '666666',
            headFill: '1F3864', zebra: 'F2F5F9', border: 'BFC7D5', codeFill: 'F3F4F6',
            noteFill: 'EAF1FB', noteBorder: '2E75B6' };
const PAGE_W = 12240, PAGE_H = 15840, MARGIN = 1080;       // Carta, márgenes 0.75"
const CONTENT_W = PAGE_W - 2 * MARGIN;                      // 10080 DXA = 7"
const PX_PER_IN = 96;
const MAX_IMG_W = 7 * PX_PER_IN, MAX_IMG_H = 8.6 * PX_PER_IN;          // página vertical
const LAND_IMG_W = 9.5 * PX_PER_IN, LAND_IMG_H = 5.9 * PX_PER_IN;       // página horizontal
const LANDSCAPE_RATIO = 1.3, LANDSCAPE_MIN_W = 800;                      // diagramas anchos -> horizontal

/* ---------- Inline: **b**, *i*, `code`, [t](u) ---------- */
function linkText(text, url) {
  let t = text;
  // Referencias a capítulos: '03_Reglas_de_Negocio.md §6' -> 'Sección 03 §6'
  t = t.replace(/`?(\d\d)_[A-Za-z0-9_\-.]+\.md`?/g, 'Sección $1');
  if (/^\d\d$/.test(t.trim())) t = `Sección ${t.trim()}`;
  return t;
}

function parseInline(text, fmt = {}) {
  const runs = [];
  const re = /(\*\*(.+?)\*\*)|(`([^`]+)`)|(\[([^\]]+)\]\(([^)]+)\))|(\*([^*\s][^*]*?)\*)/g;
  let last = 0, m;
  while ((m = re.exec(text))) {
    if (m.index > last) runs.push(run(text.slice(last, m.index), fmt));
    if (m[1]) runs.push(...parseInline(m[2], { ...fmt, bold: true }));
    else if (m[3]) runs.push(run(m[4], { ...fmt, code: true }));
    else if (m[5]) runs.push(...parseInline(linkText(m[6], m[7]), { ...fmt, link: true }));
    else if (m[8]) runs.push(...parseInline(m[9], { ...fmt, italics: true }));
    last = re.lastIndex;
  }
  if (last < text.length) runs.push(run(text.slice(last), fmt));
  return runs;
}

function run(t, fmt) {
  if (!fmt.code) t = t.replace(/\\([\\`*_{}\[\]()#+\-.!|<>~"'])/g, '$1');   // escapes de Markdown (pandoc)
  return new TextRun({
    text: t,
    style: fmt.code ? 'VerbatimChar' : undefined,   // pandoc lo reconoce como `código`
    bold: fmt.bold, italics: fmt.italics,
    font: fmt.code ? MONO : (fmt.font || FONT),
    size: fmt.code ? Math.max((fmt.size || 20) - 2, 14) : fmt.size,
    color: fmt.color || (fmt.link ? C.accent : undefined),
    shading: fmt.code && !fmt.noCodeShade ? { type: ShadingType.CLEAR, color: 'auto', fill: 'EEF0F3' } : undefined,
  });
}

/* ---------- Bloques ---------- */
let numberingInstance = 0;
const headingParas = new WeakSet();

function heading(level, text) {
  const map = { 1: HeadingLevel.HEADING_1, 2: HeadingLevel.HEADING_2, 3: HeadingLevel.HEADING_3, 4: HeadingLevel.HEADING_4 };
  const para = new Paragraph({
    heading: map[level],
    pageBreakBefore: level === 1,
    children: parseInline(text.replace(/\*\*/g, ''), {}),
  });
  headingParas.add(para);
  return para;
}

const plainParas = new WeakSet();
function paragraph(text) {
  const p = new Paragraph({ spacing: { after: 120, line: 276 }, children: parseInline(text) });
  plainParas.add(p);
  return p;
}

function note(lines) {
  return lines.map((l, i) => new Paragraph({
    style: 'BlockText',                               // pandoc lo reconoce como nota (> ...)
    spacing: { before: i === 0 ? 80 : 0, after: i === lines.length - 1 ? 160 : 40, line: 264 },
    shading: { type: ShadingType.CLEAR, color: 'auto', fill: C.noteFill },
    border: { left: { style: BorderStyle.SINGLE, size: 18, color: C.noteBorder, space: 8 } },
    children: parseInline(l, { size: 19 }),
  }));
}

function codeBlock(lines) {
  return lines.map((l, i) => new Paragraph({
    style: 'SourceCode',                              // pandoc lo reconoce como bloque de código
    spacing: { before: i === 0 ? 80 : 0, after: i === lines.length - 1 ? 160 : 0, line: 228 },
    shading: { type: ShadingType.CLEAR, color: 'auto', fill: C.codeFill },
    border: i === 0 ? { top: { style: BorderStyle.SINGLE, size: 4, color: C.border, space: 4 } }
          : i === lines.length - 1 ? { bottom: { style: BorderStyle.SINGLE, size: 4, color: C.border, space: 4 } } : undefined,
    children: [new TextRun({ text: l.length ? l : ' ', font: MONO, size: 15, color: '1F2937' })],
  }));
}

function pngSize(file) {
  const b = fs.readFileSync(file);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) };
}

function mermaidImage(src) {
  const name = `mermaid-${crypto.createHash('sha1').update(src.trim()).digest('hex').slice(0, 12)}.png`;
  const file = path.join(ROOT, cfg.imageDir, name);
  if (!fs.existsSync(file)) {
    console.warn(`  ! Falta ${name}: ejecute render_mermaid.js. Se inserta el código del diagrama.`);
    return codeBlock(src.trimEnd().split('\n'));
  }
  const { w, h } = pngSize(file);
  let dw = w / 2, dh = h / 2;                       // PNG a escala 2x
  const landscape = dw / dh > LANDSCAPE_RATIO && dw > LANDSCAPE_MIN_W;
  const s = landscape ? Math.min(1, LAND_IMG_W / dw, LAND_IMG_H / dh) : Math.min(1, MAX_IMG_W / dw, MAX_IMG_H / dh);
  dw *= s; dh *= s;
  const para = new Paragraph({
    alignment: AlignmentType.CENTER, spacing: { before: 120, after: 160 },
    children: [new ImageRun({ type: 'png', data: fs.readFileSync(file), transformation: { width: Math.round(dw), height: Math.round(dh) } })],
  });
  return landscape ? [{ landscape: true, para }] : [para];
}

function fileImage(file) {
  if (!fs.existsSync(file) || !/\.png$/i.test(file)) return [paragraph(`[imagen no encontrada: ${path.basename(file)}]`)];
  const { w, h } = pngSize(file);
  let dw = w / 2, dh = h / 2;
  const landscape = dw / dh > LANDSCAPE_RATIO && dw > LANDSCAPE_MIN_W;
  const s = landscape ? Math.min(1, LAND_IMG_W / dw, LAND_IMG_H / dh) : Math.min(1, MAX_IMG_W / dw, MAX_IMG_H / dh);
  const para = new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 120, after: 160 },
    children: [new ImageRun({ type: 'png', data: fs.readFileSync(file), transformation: { width: Math.round(dw * s), height: Math.round(dh * s) } })] });
  return landscape ? [{ landscape: true, para }] : [para];
}

function splitRow(line) {
  let s = line.trim();
  if (s.startsWith('|')) s = s.slice(1);
  if (s.endsWith('|')) s = s.slice(0, -1);
  const cells = []; let cur = ''; let inCode = false;
  for (let i = 0; i < s.length; i++) {
    const ch = s[i];
    if (ch === '`') inCode = !inCode;
    if (ch === '\\' && s[i + 1] === '|') { cur += '|'; i++; continue; }
    if (ch === '|' && !inCode) { cells.push(cur.trim()); cur = ''; continue; }
    cur += ch;
  }
  cells.push(cur.trim());
  return cells;
}

function plain(t) { return t.replace(/\*\*|`|\*/g, '').replace(/\[([^\]]+)\]\([^)]+\)/g, '$1'); }

function table(lines) {
  const header = splitRow(lines[0]);
  const body = lines.slice(2).map(splitRow);
  const n = header.length;
  body.forEach((r) => { while (r.length < n) r.push(''); r.length = n; });

  const size = n <= 3 ? 18 : n <= 5 ? 17 : n <= 6 ? 16 : 14;     // half-points
  const charW = (size / 2) * 20 * 0.56;                             // DXA aprox. por carácter
  const all = [header, ...body];
  const weight = [], minW = [];
  for (let c = 0; c < n; c++) {
    const texts = all.map((r) => plain(r[c] || ''));
    const avg = texts.reduce((a, t) => a + t.length, 0) / texts.length;
    const longestWord = Math.max(...texts.map((t) => Math.max(0, ...t.split(/[\s/]+/).map((w) => w.length))));
    weight.push(Math.min(Math.max(avg, 3), 60));
    minW.push(Math.min(longestWord * charW + 220, CONTENT_W * 0.38));
  }
  let sumMin = minW.reduce((a, b) => a + b, 0);
  let widths;
  if (sumMin >= CONTENT_W) widths = minW.map((w) => (w / sumMin) * CONTENT_W);
  else {
    const extra = CONTENT_W - sumMin, sw = weight.reduce((a, b) => a + b, 0);
    widths = minW.map((w, i) => w + (extra * weight[i]) / sw);
  }
  widths = widths.map(Math.floor);
  widths[n - 1] += CONTENT_W - widths.reduce((a, b) => a + b, 0);

  const border = { style: BorderStyle.SINGLE, size: 4, color: C.border };
  const borders = { top: border, bottom: border, left: border, right: border };
  const mkCell = (txt, i, isHead, zebra) => new TableCell({
    width: { size: widths[i], type: WidthType.DXA },
    borders,
    verticalAlign: VerticalAlign.CENTER,
    shading: isHead ? { type: ShadingType.CLEAR, color: 'auto', fill: C.headFill }
                    : zebra ? { type: ShadingType.CLEAR, color: 'auto', fill: C.zebra } : undefined,
    margins: { top: 50, bottom: 50, left: 80, right: 80 },
    children: [new Paragraph({
      spacing: { before: 0, after: 0, line: 240 },
      children: parseInline(txt, isHead ? { bold: true, color: 'FFFFFF', size, noCodeShade: true } : { size }),
    })],
  });

  return [new Table({
    width: { size: CONTENT_W, type: WidthType.DXA },
    columnWidths: widths,
    layout: TableLayoutType.FIXED,
    rows: [
      new TableRow({ tableHeader: true, cantSplit: true, children: header.map((h, i) => mkCell(h, i, true)) }),
      ...body.map((r, ri) => new TableRow({ cantSplit: true, children: r.map((t, i) => mkCell(t, i, false, ri % 2 === 1)) })),
    ],
  }), new Paragraph({ spacing: { after: 120 }, children: [] })];
}

function list(items) {
  const numbered = /^\d+\./.test(items[0].marker);
  const instance = ++numberingInstance;
  return items.map((it) => new Paragraph({
    numbering: { reference: numbered ? 'num' : 'bullets', level: Math.min(it.level, 1), instance: numbered ? instance : undefined },
    spacing: { after: 60, line: 264 },
    children: parseInline(it.text),
  }));
}

/* Inserta imágenes; las anchas van a una página horizontal junto con su título inmediato anterior. */
function pushImage(out, items) {
  for (const b of items) {
    if (b && b.landscape) {
      const blocks = [];
      while (out.length && headingParas.has(out[out.length - 1])) blocks.unshift(out.pop());
      blocks.push(b.para);
      out.push({ landscape: true, blocks, absorb: 2 });
    } else out.push(b);
  }
}

/* ---------- Markdown -> bloques ---------- */
function convert(md) {
  const out = [];
  const lines = md.replace(/\r/g, '').split('\n');
  let i = 0;
  const isListLine = (l) => /^\s*([*-]|\d+\.)\s+/.test(l);
  while (i < lines.length) {
    const l = lines[i];
    if (/^```/.test(l)) {
      const lang = l.slice(3).trim();
      const buf = []; i++;
      while (i < lines.length && !/^```/.test(lines[i])) buf.push(lines[i++]);
      i++;
      if (lang === 'mermaid') pushImage(out, mermaidImage(buf.join('\n') + '\n'));
      else out.push(...codeBlock(buf));
      continue;
    }
    if (/^\s*$/.test(l) || /^---+\s*$/.test(l) || /^<!--/.test(l)) { i++; continue; }
    const img = /^!\[[^\]]*\]\(([^)]+)\)\s*$/.exec(l.trim());
    if (img) { pushImage(out, fileImage(path.join(ROOT, 'docs', decodeURI(img[1])))); i++; continue; }
    if (/^( {4}|\t)\S/.test(l)) {                    // bloque de código indentado (salida de pandoc)
      const buf = [];
      while (i < lines.length && (/^( {4}|\t)/.test(lines[i]) || (/^\s*$/.test(lines[i]) && /^( {4}|\t)\S/.test(lines[i + 1] || '')))) {
        buf.push(lines[i].replace(/^( {4}|\t)/, '')); i++;
      }
      out.push(...codeBlock(buf));
      continue;
    }
    const h = /^(#{1,4})\s+(.*)$/.exec(l);
    if (h) { out.push(heading(h[1].length, h[2].trim())); i++; continue; }
    if (/^\s*\|/.test(l)) {
      const buf = [];
      while (i < lines.length && /^\s*\|/.test(lines[i])) buf.push(lines[i++]);
      out.push(...table(buf));
      continue;
    }
    if (/^>/.test(l)) {
      const buf = [];
      while (i < lines.length && /^>/.test(lines[i])) buf.push(lines[i++].replace(/^>\s?/, ''));
      out.push(...note(buf.filter((x) => x.trim().length)));
      continue;
    }
    if (isListLine(l)) {
      const items = [];
      const nextIsItem = (k) => { while (k < lines.length && /^\s*$/.test(lines[k])) k++; return k < lines.length && isListLine(lines[k]); };
      while (i < lines.length && (isListLine(lines[i]) || (/^\s{2,}\S/.test(lines[i]) && items.length)
             || (/^\s*$/.test(lines[i]) && items.length && nextIsItem(i)))) {
        if (/^\s*$/.test(lines[i])) { i++; continue; }
        const m = /^(\s*)([*-]|\d+\.)\s+(.*)$/.exec(lines[i]);
        if (m) items.push({ level: m[1].length >= 2 ? 1 : 0, marker: m[2], text: m[3] });
        else items[items.length - 1].text += ' ' + lines[i].trim();
        i++;
      }
      out.push(...list(items));
      continue;
    }
    const buf = [];
    while (i < lines.length && lines[i].trim() && !/^(#{1,4}\s|```|\s*\||>)/.test(lines[i]) && !isListLine(lines[i])) buf.push(lines[i++].trim());
    out.push(paragraph(buf.join(' ')));
  }
  // Hasta 2 párrafos cortos que siguen a un diagrama horizontal viajan con él (evita páginas casi vacías)
  for (let k = 0; k < out.length; k++) {
    const blk = out[k];
    if (!blk || !blk.landscape) continue;
    while (blk.absorb > 0 && out[k + 1] instanceof Paragraph && !headingParas.has(out[k + 1]) && plainParas.has(out[k + 1])) {
      blk.blocks.push(out.splice(k + 1, 1)[0]);
      blk.absorb--;
    }
  }
  return out;
}

/* ---------- Portada, control del documento, índice ---------- */
function simpleTable(head, rows, widths) {
  const lines = ['| ' + head.join(' | ') + ' |', '|' + head.map(() => '---').join('|') + '|',
    ...rows.map((r) => '| ' + r.join(' | ') + ' |')];
  const t = table(lines);
  if (widths) { /* anchos fijos para tablas administrativas */
    const tbl = new Table({
      width: { size: CONTENT_W, type: WidthType.DXA }, columnWidths: widths, layout: TableLayoutType.FIXED,
      rows: [head, ...rows].map((r, ri) => new TableRow({
        tableHeader: ri === 0,
        children: r.map((txt, ci) => new TableCell({
          width: { size: widths[ci], type: WidthType.DXA },
          borders: Object.fromEntries(['top', 'bottom', 'left', 'right'].map((k) => [k, { style: BorderStyle.SINGLE, size: 4, color: C.border }])),
          shading: ri === 0 ? { type: ShadingType.CLEAR, color: 'auto', fill: C.headFill } : undefined,
          margins: { top: 80, bottom: 80, left: 100, right: 100 },
          children: [new Paragraph({ children: [new TextRun({ text: txt, font: FONT, size: 18, bold: ri === 0, color: ri === 0 ? 'FFFFFF' : C.text })] })],
        })),
      })),
    });
    return [tbl, new Paragraph({ spacing: { after: 160 }, children: [] })];
  }
  return t;
}

function coverPage() {
  const p = (text, opts = {}) => new Paragraph({
    alignment: opts.align || AlignmentType.LEFT, spacing: { before: opts.before || 0, after: opts.after || 120 },
    border: opts.border, children: [new TextRun({ text, font: FONT, size: opts.size || 22, bold: opts.bold, color: opts.color || C.text })],
  });
  return [
    p(cfg.organization, { before: 1600, size: 20, color: C.muted, after: 600 }),
    p(cfg.title, { size: 56, bold: true, color: C.primary, after: 200 }),
    p(cfg.subtitle, { size: 30, color: C.accent, after: 300,
      border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: C.accent, space: 8 } } }),
    p(cfg.scope, { size: 22, after: 1400 }),
    ...simpleTable(['Atributo', 'Valor'], [
      ['Versión', cfg.version], ['Estatus', cfg.status], ['Fecha', cfg.date],
      ['Autor', cfg.author], ['Clasificación', cfg.classification],
    ], [2600, CONTENT_W - 2600]),
  ];
}

function controlSection() {
  const h = (t) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun({ text: t })] });
  return [
    new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: true, children: [new TextRun({ text: 'Control del documento' })] }),
    h('Historial de versiones'),
    ...simpleTable(['Versión', 'Fecha', 'Autor', 'Descripción del cambio'], cfg.history, [1200, 1500, 2200, CONTENT_W - 4900]),
    h('Revisión y aprobación'),
    ...simpleTable(['Rol', 'Nombre', 'Firma', 'Fecha'], cfg.approvers.map(([r, n]) => [r, n, '', '']), [3300, 2900, 2300, CONTENT_W - 8500]),
    h('Fuentes del documento'),
    paragraph('Este documento Word es la FUENTE DE VERDAD del proyecto. Las revisiones (comentarios y control de cambios) se hacen aquí. Los archivos Markdown de trabajo del repositorio (carpeta docs/) se derivan de este Word con tools/docx/docx_to_md.py, y los scripts de base de datos (database/*.sql) y la solución .NET (src/) se mantienen alineados con esta especificación.'),
    new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: true, children: [new TextRun({ text: 'Contenido' })] }),
    new TableOfContents('Contenido', { hyperlink: true, headingStyleRange: '1-3' }),
  ];
}

/* ---------- Documento ---------- */
(async () => {
  const children = [...coverPage(), ...controlSection()];
  for (const rel of cfg.chapters) {
    const md = fs.readFileSync(path.join(ROOT, rel), 'utf8');
    children.push(...convert(md));
    console.log('  + capítulo', rel);
  }

  // Estilos de título: se ajustan los predeterminados (sin duplicar styleId) para que Word y pandoc los reconozcan.
  const heading = (size, color, before, after, extra = {}) => ({
    run: { font: FONT, size, bold: true, color },
    paragraph: { spacing: { before, after }, keepNext: true, keepLines: true, ...extra },
  });

  /* Encabezado/pie reutilizables por sección */
  const tab = () => new PositionalTab({ alignment: PositionalTabAlignment.RIGHT, relativeTo: PositionalTabRelativeTo.MARGIN, leader: PositionalTabLeader.NONE });
  const mkHeader = () => new Header({ children: [new Paragraph({
    border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: C.border, space: 4 } },
    children: [
      new TextRun({ text: 'JuP1 · ME/QMM · Process Excellence', font: FONT, size: 16, color: C.muted }),
      new TextRun({ children: [tab(), `${cfg.title} · SRS v${cfg.version}`], font: FONT, size: 16, color: C.muted }),
    ] })] });
  const mkFooter = () => new Footer({ children: [new Paragraph({
    children: [
      new TextRun({ text: `${cfg.classification} · ${cfg.status}`, font: FONT, size: 16, color: C.muted }),
      new TextRun({ children: [tab(), 'Página ', PageNumber.CURRENT, ' de ', PageNumber.TOTAL_PAGES], font: FONT, size: 16, color: C.muted }),
    ] })] });
  const margin = { top: MARGIN, bottom: MARGIN, left: MARGIN, right: MARGIN, header: 500, footer: 500 };

  /* Divide el contenido en secciones: vertical por defecto, horizontal para diagramas anchos */
  const sections = [];
  let current = [];
  const flush = (first = false) => {
    if (!current.length) return;
    sections.push({
      properties: { page: { size: { width: PAGE_W, height: PAGE_H }, margin }, titlePage: first },
      headers: first ? { default: mkHeader(), first: new Header({ children: [new Paragraph({ children: [] })] }) } : { default: mkHeader() },
      footers: first ? { default: mkFooter(), first: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: cfg.classification, font: FONT, size: 16, color: C.muted })] })] }) } : { default: mkFooter() },
      children: current,
    });
    current = [];
  };
  for (const b of children) {
    if (b && b.landscape) {
      flush(sections.length === 0);
      sections.push({
        properties: { page: { size: { width: PAGE_W, height: PAGE_H, orientation: PageOrientation.LANDSCAPE }, margin } },
        headers: { default: mkHeader() }, footers: { default: mkFooter() },
        children: b.blocks,
      });
    } else current.push(b);
  }
  flush(sections.length === 0);

  const doc = new Document({
    creator: cfg.author, title: `${cfg.title} · SRS v${cfg.version}`, description: cfg.subtitle,
    features: { updateFields: true },
    styles: {
      default: {
        document: { run: { font: FONT, size: 20, color: C.text } },
        heading1: heading(32, C.primary, 0, 200, { outlineLevel: 0, border: { bottom: { style: BorderStyle.SINGLE, size: 8, color: C.primary, space: 4 } } }),
        heading2: heading(26, C.primary, 280, 120, { outlineLevel: 1 }),
        heading3: heading(22, C.accent, 220, 100, { outlineLevel: 2 }),
        heading4: heading(20, C.text, 180, 80, { outlineLevel: 3 }),
      },
      // Sin un estilo "Normal" explícito, pandoc no resuelve la herencia y no reconoce los títulos.
      paragraphStyles: [
        { id: 'Normal', name: 'Normal', quickFormat: true, run: { font: FONT, size: 20, color: C.text } },
        { id: 'SourceCode', name: 'Source Code', basedOn: 'Normal', run: { font: MONO, size: 15 } },
        { id: 'BlockText', name: 'Block Text', basedOn: 'Normal', run: { font: FONT, size: 19 } },
      ],
      characterStyles: [{ id: 'VerbatimChar', name: 'Verbatim Char', run: { font: MONO } }],
    },
    numbering: {
      config: [
        { reference: 'bullets', levels: [
          { level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 360, hanging: 260 } } } },
          { level: 1, format: LevelFormat.BULLET, text: '–', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 260 } } } } ] },
        { reference: 'num', levels: [
          { level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 360, hanging: 300 } } } },
          { level: 1, format: LevelFormat.LOWER_LETTER, text: '%2)', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 300 } } } } ] },
      ],
    },
    sections,
  });


  const out = process.env.DOCX_OUTPUT ? path.resolve(process.env.DOCX_OUTPUT) : path.join(ROOT, cfg.output);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, await Packer.toBuffer(doc));
  console.log('OK ->', path.relative(ROOT, out));
})().catch((e) => { console.error(e); process.exit(1); });
