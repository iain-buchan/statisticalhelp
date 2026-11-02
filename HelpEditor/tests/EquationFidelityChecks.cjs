// Compare visible original artwork with Flare SVGs. Hidden MathJax output is
// accessibility content and must never stand in for the visible image here.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const { chromium } = require('./browser-runtime.cjs');
const root = path.resolve(__dirname, '..'), source = path.join(root, 'HelpSource/Content'), evidence = path.join(root, 'evidence');
const baseline = process.env.STATSDIRECT_FLARE_DESKTOP || 'C:/Develop/SD3Help/StatsDirect/Output/ieb21/DesktopHelp';
const output = path.join(root, 'Build/Desktop');
const topics = [];
function walk(dir) { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { const file = path.join(dir, e.name); if (e.isDirectory()) walk(file); else if (/\.htm$/i.test(e.name) && /<MadCap:equation\b/i.test(fs.readFileSync(file, 'utf8'))) topics.push(path.relative(source, file).replaceAll('\\', '/').toLowerCase()); } }
walk(source);
const mime = { '.htm': 'text/html', '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.gif': 'image/gif', '.jpg': 'image/jpeg', '.json': 'application/json', '.xml': 'application/xml', '.woff': 'font/woff', '.woff2': 'font/woff2' };
const server = http.createServer((req, res) => {
  const parts = decodeURIComponent(new URL(req.url, 'http://localhost').pathname).split('/').filter(Boolean), mount = parts.shift() === 'flare' ? baseline : output, file = path.resolve(mount, ...parts);
  if (!file.startsWith(path.resolve(mount) + path.sep)) { res.writeHead(403).end(); return; }
  try { res.setHeader('Content-Type', (mime[path.extname(file).toLowerCase()] || 'application/octet-stream') + '; charset=utf-8'); res.end(fs.readFileSync(file)); } catch { res.writeHead(404).end(); }
});
async function inspect(page, url, isBaseline) {
  await page.goto(url+(isBaseline?'':'?content=1'), { waitUntil: 'load' });
  if (!isBaseline) await page.evaluate(() => MathJax.startup.promise);
  await page.waitForFunction(() => [...document.images].every(i => i.complete));
  return page.evaluate(isBaseline => {
    const container = document.getElementById('mc-main-content') || document.body;
    container.style.width = '900px'; container.style.maxWidth = 'none'; container.style.boxSizing = 'content-box';
    const selector = isBaseline ? 'img[src*="generatedimages/equations/"]' : '.help-equation';
    return [...container.querySelectorAll(selector)].filter(e => e.getBoundingClientRect().width > 0 && e.getBoundingClientRect().height > 0).map(e => {
      const image=e.querySelector(':scope>img.help-equation-original'),visible=image||e,r = visible.getBoundingClientRect(), svg = image?null:e.querySelector('svg'), sr = svg?.getBoundingClientRect();
      return { width: r.width, height: r.height, src: visible.getAttribute('src'), cachedArtwork:!!image,imageLoaded:visible.tagName!=='IMG'||visible.complete&&visible.naturalWidth>0,text: e.textContent.slice(0, 160), svg: sr ? { width: sr.width, height: sr.height } : null, scrollWidth: visible.scrollWidth, errors: e.querySelectorAll('[data-mml-node="merror"]').length };
    });
  }, isBaseline);
}
(async () => {
  let browser; const results = [], failures = [], pageErrors = [], blocked = new Set();
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); const origin = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1400, height: 1100 } });
    await context.route('**/*', route => { const u = route.request().url(); if (u.startsWith(origin + '/') || /^(data|blob):/.test(u)) return route.continue(); blocked.add(u); return route.abort(); });
    let cursor = 0;
    await Promise.all(Array.from({ length: 5 }, async () => {
      const oldPage = await context.newPage(), newPage = await context.newPage();
      for (const [target, page] of [['flare', oldPage], ['editor', newPage]]) page.on('pageerror', e => pageErrors.push({ target, message: e.message }));
      while (cursor < topics.length) {
        const topic = topics[cursor++];
        try {
          const oldEquations = await inspect(oldPage, `${origin}/flare/${topic}`, true), newEquations = await inspect(newPage, `${origin}/editor/${topic}`, false);
          if (oldEquations.length !== newEquations.length) failures.push({ topic, type: 'Equation count differs', baseline: oldEquations.length, output: newEquations.length });
          for (let i = 0; i < oldEquations.length; i++) {
            const before = oldEquations[i], after = newEquations[i]; if (!after) continue;
            const dx = after.width - before.width, dy = after.height - before.height, widthPercent = dx / before.width * 100, heightPercent = dy / before.height * 100;
            const materialOutlier = (Math.abs(widthPercent) > 15 && Math.abs(dx) > 5) || (Math.abs(heightPercent) > 15 && Math.abs(dy) > 5);
            const largeAbsoluteDifference = Math.abs(dy) > 13 || Math.abs(dx) > 25;
            const row = { topic, equation: i + 1, before, after, widthDifference: dx, heightDifference: dy, widthPercent, heightPercent, materialOutlier, largeAbsoluteDifference };
            results.push(row);
            if (after.errors) failures.push({ topic, equation: i + 1, type: 'Rendered math error' });
            if(!after.imageLoaded)failures.push({topic,equation:i+1,type:'Visible equation image did not load'});
            if(!after.cachedArtwork)failures.push({topic,equation:i+1,type:'Unchanged baseline equation did not use its source-verified artwork'});
            if(Math.abs(dx)>.02||Math.abs(dy)>.02)failures.push({topic,equation:i+1,type:'Visible original artwork geometry differs',dx,dy});
            if (materialOutlier || largeAbsoluteDifference) {
              const name = `equation-fidelity-${topic.replaceAll('/', '-').replace('.htm', '')}-${i + 1}`;
              await oldPage.locator('#mc-main-content img[src*="generatedimages/equations/"]').nth(i).screenshot({ path: path.join(evidence, name + '-flare.png') });
              await newPage.locator('.help-equation').nth(i).locator('img.help-equation-original,svg').first().screenshot({ path: path.join(evidence, name + '-editor.png') });
            }
          }
        } catch (error) { failures.push({ topic, error: error.stack }); }
      }
      await oldPage.close(); await newPage.close();
    }));
    const passed = failures.length === 0 && pageErrors.length === 0 && results.length === 258;
    const report = { passed, checkedAtUtc: new Date().toISOString(), method: 'All 258 visible source-verified original SVG images at matched 900px content width; image loading, artwork use, hidden renderer errors and exact visible dimensions (0.02px rounding tolerance) are required. Separate all-equation screenshot checks establish pixel identity.', topics: topics.length, equations: results.length, materialOutliers: results.filter(r => r.materialOutlier), differencesOverFivePixels: results.filter(r => Math.abs(r.widthDifference) > 5 || Math.abs(r.heightDifference) > 5).length, failures, pageErrors, externalBlocked: [...blocked], results };
    fs.writeFileSync(path.join(evidence, 'equation-fidelity-checks.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ ...report, results: undefined }, null, 2)); if (!passed) process.exitCode = 1;
  } finally { if (browser) await browser.close(); server.close(); }
})().catch(e => { console.error(e); server.close(); process.exitCode = 1; });
