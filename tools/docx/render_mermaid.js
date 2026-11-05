#!/usr/bin/env node
/*
 * Renderiza a PNG cada bloque ```mermaid de los capítulos configurados.
 * Nombre de archivo = hash del código del diagrama, así build_docx.js
 * encuentra la imagen correcta y un diagrama editado se re-renderiza.
 *
 * Uso:  MERMAID_JS=/ruta/mermaid.min.js node tools/docx/render_mermaid.js
 *       (por defecto busca tools/docx/node_modules/mermaid/dist/mermaid.min.js)
 * Requiere: playwright (Chromium) y mermaid (npm).
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '..', '..');
const cfg = JSON.parse(fs.readFileSync(path.join(__dirname, 'document.config.json'), 'utf8'));
const MERMAID_JS = process.env.MERMAID_JS || path.join(__dirname, 'node_modules', 'mermaid', 'dist', 'mermaid.min.js');
const outDir = path.join(ROOT, cfg.imageDir);

const hash = (src) => crypto.createHash('sha1').update(src.trim()).digest('hex').slice(0, 12);

function extractDiagrams() {
  const list = [];
  for (const rel of cfg.chapters) {
    const md = fs.readFileSync(path.join(ROOT, rel), 'utf8');
    const re = /```mermaid\n([\s\S]*?)```/g;
    let m;
    while ((m = re.exec(md))) list.push({ src: m[1], file: rel });
  }
  return list;
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const diagrams = extractDiagrams();
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1400, height: 1000 }, deviceScaleFactor: 2 });
  await page.setContent('<html><body style="margin:0;background:#fff"><div id="c" style="width:1100px;padding:16px"></div></body></html>');
  await page.addScriptTag({ path: MERMAID_JS });
  await page.evaluate(() => window.mermaid.initialize({
    startOnLoad: false, theme: 'neutral', fontFamily: 'Arial',
    themeVariables: { fontSize: '18px' },
    flowchart: { htmlLabels: true, useMaxWidth: false, nodeSpacing: 30, rankSpacing: 40, padding: 10 },
    er: { useMaxWidth: false, fontSize: 16 },
    state: { useMaxWidth: false },
    gantt: { useMaxWidth: false, fontSize: 14, sectionFontSize: 14, barHeight: 24, barGap: 6, leftPadding: 220 }
  }));

  let i = 0;
  for (const d of diagrams) {
    const name = `mermaid-${hash(d.src)}.png`;
    const target = path.join(outDir, name);
    if (fs.existsSync(target) && !process.env.FORCE) { console.log('ok (cache)', name, d.file); continue; }
    const svg = await page.evaluate(async ([id, src]) => {
      const { svg } = await window.mermaid.render(id, src);
      return svg;
    }, [`d${i++}`, d.src]);
    await page.evaluate((s) => { document.getElementById('c').innerHTML = s; }, svg);
    const el = await page.$('#c svg');
    await el.screenshot({ path: target, omitBackground: false });
    console.log('rendered', name, d.file);
  }
  await browser.close();

  // Limpia imágenes de diagramas que ya no existen en los capítulos
  const keep = new Set(diagrams.map((d) => `mermaid-${hash(d.src)}.png`));
  for (const f of fs.readdirSync(outDir)) {
    if (/^mermaid-[0-9a-f]{12}\.png$/.test(f) && !keep.has(f)) { fs.unlinkSync(path.join(outDir, f)); console.log('removed', f); }
  }
})().catch((e) => { console.error(e); process.exit(1); });
