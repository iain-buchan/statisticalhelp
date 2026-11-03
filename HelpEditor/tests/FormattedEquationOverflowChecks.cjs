// Inspect the editable MathLive widgets, independently of generated help SVGs.
// Topics are read only; all editing/Undo happens in an isolated headless page.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const { chromium } = require('./browser-runtime.cjs');
const root = path.resolve(__dirname, '..'), web = path.join(root, 'src/StatsDirectHelpEditor.App/Web');
const content = path.join(root, 'HelpSource/Content'), evidence = path.join(root, 'evidence/FormattedEquationOverflow');
const before = process.argv.includes('--baseline'), checks = [], renders = [], errors = [], external = [];
const suffix = before ? 'before' : 'after'; fs.mkdirSync(evidence, { recursive: true });
const SDH = 'https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd';
const mime = { '.html': 'text/html', '.htm': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.woff': 'font/woff' };
const server = http.createServer((req, res) => {
  const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname), project = pathname.startsWith('/project/'), base = project ? content : web;
  const file = path.resolve(base, project ? pathname.slice(9) : pathname === '/' ? 'index.html' : pathname.slice(1));
  if (!file.startsWith(base + path.sep)) { res.writeHead(403).end(); return; }
  try { res.setHeader('Content-Type', (mime[path.extname(file)] || 'application/octet-stream') + ';charset=utf-8'); res.end(fs.readFileSync(file)); }
  catch { res.writeHead(404).end(); }
});
const term = '\\frac{(x_j-\\bar{x})^2}{1+\\sqrt{(y_j-\\bar{y})^2}}';
const synthetic = '<html xmlns:sdh="' + SDH + '"><head><title>Formatted equation geometry</title></head><body>' +
  '<p id="short">Short <sdh:equation>$\\frac{a+b}{c+d}$</sdh:equation> formula.</p>' +
  '<p id="tall"><sdh:equation>$\\displaystyle \\left\\{\\begin{array}{l}' + Array.from({ length: 12 }, (_, i) => '\\frac{x_' + i + '^2}{\\sqrt{y_' + i + '+1}}').join(' \\\\ ') + '\\end{array}\\right.$</sdh:equation></p><p id="after-tall">Text after twelve rows.</p>' +
  '<p id="long"><sdh:equation>$\\displaystyle ' + Array(12).fill(term).join('+') + '$</sdh:equation></p><p id="after-long">Text after the long equation.</p></body></html>';
function inspect() {
  const panel = document.getElementById('visual-panel'), panelBox = panel.getBoundingClientRect();
  const box = r => ({ left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height });
  return [...document.querySelectorAll('.equation-widget')].map((widget, index) => {
    const rect = widget.getBoundingClientRect(), style = getComputedStyle(widget), glyphs = [];
    const walker = document.createTreeWalker(widget, NodeFilter.SHOW_TEXT);
    for (let node; (node = walker.nextNode());) {
      if (!node.nodeValue.trim()) continue;
      const range = document.createRange(); range.selectNodeContents(node);
      for (const r of range.getClientRects()) if (r.width && r.height) glyphs.push(box(r));
    }
    for (const svg of widget.querySelectorAll('svg')) {
      const bounds = svg.getBBox(), matrix = svg.getScreenCTM();
      if (!matrix) continue;
      const points = [[bounds.x, bounds.y], [bounds.x + bounds.width, bounds.y], [bounds.x, bounds.y + bounds.height], [bounds.x + bounds.width, bounds.y + bounds.height]].map(([x, y]) => new DOMPoint(x, y).matrixTransform(matrix));
      glyphs.push({ left: Math.min(...points.map(p => p.x)), right: Math.max(...points.map(p => p.x)), top: Math.min(...points.map(p => p.y)), bottom: Math.max(...points.map(p => p.y)) });
    }
    const ink = glyphs.length ? { left: Math.min(...glyphs.map(r => r.left)), right: Math.max(...glyphs.map(r => r.right)), top: Math.min(...glyphs.map(r => r.top)), bottom: Math.max(...glyphs.map(r => r.bottom)) } : null;
    const scrollers = [widget, ...widget.querySelectorAll('*')].flatMap(element => {
      const s = getComputedStyle(element), x = /auto|scroll|overlay/.test(s.overflowX) && element.scrollWidth > element.clientWidth, y = /auto|scroll|overlay/.test(s.overflowY) && element.scrollHeight > element.clientHeight;
      return x || y ? [{ tag: element.localName, className: element.className, x, y, clientWidth: element.clientWidth, scrollWidth: element.scrollWidth, clientHeight: element.clientHeight, scrollHeight: element.scrollHeight }] : [];
    });
    const clips = [];
    if (ink) for (let element = widget; element && element !== panel; element = element.parentElement) {
      const s = getComputedStyle(element), r = element.getBoundingClientRect();
      const left = r.left + element.clientLeft, top = r.top + element.clientTop;
      if (/auto|scroll|hidden|clip/.test(s.overflowX) && (ink.left < left - .75 || ink.right > left + element.clientWidth + .75)) clips.push({ tag: element.localName, id: element.id, axis: 'x' });
      if (/auto|scroll|hidden|clip/.test(s.overflowY) && (ink.top < top - .75 || ink.bottom > top + element.clientHeight + .75)) clips.push({ tag: element.localName, id: element.id, axis: 'y' });
    }
    const parent = widget.closest('p'), previous = parent?.previousElementSibling?.getBoundingClientRect(), next = parent?.nextElementSibling?.getBoundingClientRect();
    return { index: index + 1, rect: box(rect), ink, glyphBoxes: glyphs.length, widgetOverflow: { x: style.overflowX, y: style.overflowY }, scrollers, clips,
      noFollowingOverlap: !next || !ink || next.top >= ink.bottom - .75,
      noPrecedingOverlap: !previous || !ink || previous.bottom <= ink.top + .75,
      accessibleThroughPanel: !!ink && ink.left >= panelBox.left + panel.clientLeft - panel.scrollLeft - .75 && ink.right <= panelBox.left + panel.clientLeft + panel.scrollWidth - panel.scrollLeft + .75 && ink.top >= panelBox.top + panel.clientTop - panel.scrollTop - .75 && ink.bottom <= panelBox.top + panel.clientTop + panel.scrollHeight - panel.scrollTop + .75,
      panel: { clientWidth: panel.clientWidth, scrollWidth: panel.scrollWidth, clientHeight: panel.clientHeight, scrollHeight: panel.scrollHeight },
      error: widget.classList.contains('equation-error') };
  });
}
function record(name, passed, detail) { checks.push({ name, passed: !!passed, detail }); }
(async () => {
  let browser;
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); const origin = 'http://127.0.0.1:' + server.address().port;
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 1000 } });
    await context.route('**/*', route => { const url = route.request().url(); if (url.startsWith(origin + '/') || /^(data|blob):/.test(url)) return route.continue(); external.push(url); return route.abort(); });
    const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
    await page.goto(origin + '/'); await page.waitForFunction(() => !!window.HelpEditor); await page.evaluate(() => document.fonts.ready);
    const real = fs.readFileSync(path.join(content, 'chi_square_tests/2k.htm'), 'utf8');
    for (const width of [3840, 1280, 390]) for (const [name, source, expected] of [['2k', real, 2], ['synthetic', synthetic, 3]]) {
      await page.setViewportSize({ width, height: width === 3840 ? 2160 : 1000 });
      await page.evaluate(payload => HelpEditor.load(payload), { path: name === '2k' ? 'chi_square_tests/2k.htm' : 'isolated/formatted-overflow.htm', title: name, baseUrl: origin + '/project/chi_square_tests/2k.htm', source });
      await page.evaluate(() => document.fonts.ready);
      const equations = await page.evaluate(inspect); renders.push({ name, width, equations });
      record(name + ' MathLive ink has no nested scroller clipping or prose overlap at ' + width + 'px', equations.length === expected && equations.every(e => e.ink && e.glyphBoxes > 0 && !e.scrollers.length && !e.clips.length && !e.error && e.noFollowingOverlap && e.noPrecedingOverlap && e.accessibleThroughPanel), { equations });
      record(name + ' Formatted inspection leaves source exact at ' + width + 'px', await page.evaluate(source => HelpEditor.getSource() === source && !HelpEditor.getState().dirty, source));
      await page.locator('.equation-widget').nth(1).scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, name + '-' + width + '-' + suffix + '.png') });
      if (name === 'synthetic' && width === 1280) {
        await page.locator('.equation-widget').nth(2).scrollIntoViewIfNeeded();
        const reachable = await page.evaluate(() => {
          const panel = document.getElementById('visual-panel'), equation = document.querySelectorAll('.equation-widget')[2]; panel.scrollLeft = panel.scrollWidth;
          const panelBox = panel.getBoundingClientRect(), nodes = [...equation.querySelectorAll('*')].filter(e => e.childNodes.length && [...e.childNodes].some(n => n.nodeType === Node.TEXT_NODE && n.nodeValue.trim()));
          const last = nodes.at(-1)?.getBoundingClientRect();
          return { oversized: equation.getBoundingClientRect().width > panel.clientWidth, scrollablePage: panel.scrollWidth > panel.clientWidth && panel.scrollLeft > 0, lastGlyphReachable: !!last && last.right <= panelBox.right + .75 && last.right >= panelBox.left };
        });
        record('Long equation right edge is reachable through normal page scrolling', Object.values(reachable).every(Boolean), reachable);
        await page.screenshot({ path: path.join(evidence, 'long-right-edge-' + suffix + '.png') });
      }
    }
    await page.setViewportSize({ width: 1280, height: 1000 });
    const payload = { path: 'chi_square_tests/2k.htm', title: '2k editing', baseUrl: origin + '/project/chi_square_tests/2k.htm', source: real };
    await page.evaluate(payload => HelpEditor.load(payload), payload);
    const second = page.locator('.equation-widget').nth(1); await second.dblclick();
    record('Tall equation remains editable by double-click and Cancel preserves source', await page.evaluate(source => { const editable = document.getElementById('equation-dialog').open && document.getElementById('equation-field').errors.length === 0; document.getElementById('equation-cancel').click(); return editable && HelpEditor.getSource() === source; }, real));
    await second.focus(); await page.keyboard.press('Enter');
    record('Tall equation remains keyboard accessible', await page.evaluate(() => document.getElementById('equation-dialog').open));
    await page.locator('#latex-options summary').click(); await page.fill('#equation-latex', '\\frac{7}{3}+\\alpha'); await page.click('#equation-apply');
    const editHistory = await page.evaluate(source => { const edited = HelpEditor.getSource(), changed = edited !== source && edited.includes('\\alpha'); HelpEditor.undo(); const undo = HelpEditor.getSource() === source; HelpEditor.redo(); const redo = HelpEditor.getSource() === edited; HelpEditor.undo(); return { changed, undo, redo, clean: !HelpEditor.getState().dirty }; }, real);
    record('Tall equation edit and Undo restore exact original source', Object.values(editHistory).every(Boolean), editHistory);
    record('No browser errors or external requests', !errors.length && !external.length, { errors, external });
    await context.close();
  } catch (error) { record('Formatted equation regression completed', false, error.stack || String(error)); }
  finally {
    if (browser) await browser.close(); server.close();
    const report = { checkedAtUtc: new Date().toISOString(), baseline: before, passed: checks.length > 0 && checks.every(check => check.passed), checks, renders, errors, external, method: 'MathLive editable HTML text-range glyph boxes plus SVG ink geometry; nested scrolling/clipping, following-flow overlap and outer-panel reachability. Original help source is never written.' };
    fs.writeFileSync(path.join(evidence, suffix + '.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ passed: report.passed, checks: checks.length, failures: checks.filter(check => !check.passed) }, null, 2)); if (!report.passed) process.exitCode = 1;
  }
})();
