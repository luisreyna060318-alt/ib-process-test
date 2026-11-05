#!/usr/bin/env node
/*
 * Renderiza a PNG los diagramas Mermaid del proyecto.
 *
 *   - docs/diagrams/*.mmd                -> docs/diagrams/*.png   (fuentes de los diagramas del Word)
 *   - bloques ```mermaid en los capítulos -> docs/img/mermaid-<hash>.png (sólo si se genera el Word desde .md)
 *
 * Uso:  MERMAID_JS=/ruta/mermaid.min.js node tools/docx/render_mermaid.js
 *       (por defecto busca tools/docx/node_modules/mermaid/dist/mermaid.min.js)
 * Requiere: playwright (Chromium) y mermaid (npm).
 * Para actualizar un diagrama en el Word: edite el .mmd, ejecute este script y reemplace la imagen en el .docx.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '..', '..');
const cfg = JSON.parse(fs.readFileSync(path.join(__dirname, 'document.config.json'), 'utf8'));
const MERMAID_JS = process.env.MERMAID_JS || path.join(__dirname, 'node_modules', 'mermaid', 'dist', 'mermaid.min.js');
const imgDir = path.join(ROOT, cfg.imageDir);
const diagramDir = path.join(ROOT, 'docs', 'diagrams');

const hash = (src) => crypto.createHash('sha1').update(src.trim()).digest('hex').slice(0, 12);

function collect() {
  const jobs = [];
  if (fs.existsSync(diagramDir))
    for (const f of fs.readdirSync(diagramDir).filter((f) => f.endsWith('.mmd')).sort())
      jobs.push({ src: fs.readFileSync(path.join(diagramDir, f), 'utf8'), target: path.join(diagramDir, f.replace(/\.mmd$/, '.png')), label: `docs/diagrams/${f}` });
  for (const rel of cfg.chapters) {
    const file = path.join(ROOT, rel);
    if (!fs.existsSync(file)) continue;
    const re = /```mermaid\n([\s\S]*?)```/g;
    let m;
    while ((m = re.exec(fs.readFileSync(file, 'utf8'))))
      jobs.push({ src: m[1], target: path.join(imgDir, `mermaid-${hash(m[1])}.png`), label: rel, cached: true });
  }
  return jobs;
}

(async () => {
  const jobs = collect();
  if (!jobs.length) { console.log('No hay diagramas que renderizar.'); return; }
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
    gantt: { useMaxWidth: false, fontSize: 14, sectionFontSize: 14, barHeight: 24, barGap: 6, leftPadding: 220 },
  }));

  let i = 0;
  for (const job of jobs) {
    if (job.cached && fs.existsSync(job.target) && !process.env.FORCE) { console.log('ok (cache)', job.label); continue; }
    fs.mkdirSync(path.dirname(job.target), { recursive: true });
    const svg = await page.evaluate(async ([id, src]) => (await window.mermaid.render(id, src)).svg, [`d${i++}`, job.src]);
    await page.evaluate((s) => { document.getElementById('c').innerHTML = s; }, svg);
    await (await page.$('#c svg')).screenshot({ path: job.target });
    console.log('rendered', path.relative(ROOT, job.target));
  }
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
