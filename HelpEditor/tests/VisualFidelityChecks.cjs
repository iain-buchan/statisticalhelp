// Read-only comparison of rendered topic layout, separately from help navigation.
// No pixel-equivalence claim: MathJax SVG and Flare's generated equation SVG differ.
const fs = require('node:fs'), path = require('node:path'), http = require('node:http');
const { chromium } = require('./browser-runtime.cjs');
const { resolveFlareRoot } = require('./flare-baseline.cjs');
const root = path.resolve(__dirname, '..'), evidence = path.join(root, 'evidence');
const flare = resolveFlareRoot();
const mounts = { DesktopHelp: path.join(flare, 'DesktopHelp'), Website: path.join(flare, 'Website'), GitHub: path.join(flare, 'GitHub'), Desktop: path.join(root, 'Build/Desktop'), Public: path.join(root, 'Build/Public') };
const mime = { '.htm': 'text/html', '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.gif': 'image/gif', '.jpg': 'image/jpeg', '.json': 'application/json', '.xml': 'application/xml', '.woff': 'font/woff', '.woff2': 'font/woff2' };
const server = http.createServer((req, res) => {
  const parts = decodeURIComponent(new URL(req.url, 'http://localhost').pathname).split('/').filter(Boolean), mount = mounts[parts.shift()];
  const file = mount && path.resolve(mount, ...parts);
  if (!file || !file.startsWith(path.resolve(mount) + path.sep)) { res.writeHead(403).end(); return; }
  try { res.setHeader('Content-Type', (mime[path.extname(file).toLowerCase()] || 'application/octet-stream') + '; charset=utf-8'); res.end(fs.readFileSync(file)); } catch { res.writeHead(404).end(); }
});
const topics = [
  'rates/incidence_and_prevalence.htm',
  'analysis_of_variance/one_way.htm',
  'basic_descriptive_statistics/time_series.htm',
  'graphics/box_whisker.htm',
  'parametric_methods/paired_t.htm',
  'references/reference_list.htm'
];
const pairs = [['DesktopHelp', 'Desktop'], ['Website', 'Public'], ['GitHub', 'Public']];
const checks = [];
const near = (a, b) => Math.abs(Number(a) - Number(b)) < 0.1;
function check(pair, topic, name, passed, detail) { checks.push({ baseline: pair[0], output: pair[1], topic, name, passed: !!passed, detail }); }
function typographyDifferences(before, after, extra = []) {
  if (!before || !after) return before === after ? [] : [{ property: 'element', before, after }];
  const differences = [];
  // Flare's generic sans-serif and the explicit Arial default resolve to the
  // same Windows font. Compare measured glyph advances, not family spelling.
  if (!near(before.fontProbeWidth, after.fontProbeWidth)) differences.push({ property: 'renderedFontGlyphWidth', before: { family: before.font, width: before.fontProbeWidth }, after: { family: after.font, width: after.fontProbeWidth } });
  for (const property of ['fontSize', 'fontWeight', 'fontStyle', 'lineHeight', 'marginTop', 'marginBottom', 'marginLeft', 'marginRight', 'color', ...extra])
    if (before[property] !== after[property]) differences.push({ property, before: before[property], after: after[property] });
  return differences;
}
function verifyPair(pair, topic, before, after) {
  for (const property of ['h1', 'paragraph']) {
    const differences = typographyDifferences(before[property], after[property], ['background']);
    check(pair, topic, property === 'h1' ? 'Heading typography matches the Flare default' : 'Paragraph typography matches the Flare default', differences.length === 0, differences);
  }
  check(pair, topic, 'Heading rendered height matches Flare', near(before.h1.height, after.h1.height), { before: before.h1.height, after: after.h1.height });
  const figures = before.figures.map((figure, i) => {
    const rendered = after.figures[i];
    return { source: figure.src, before: { width: figure.width, height: figure.height, naturalWidth: figure.naturalWidth, naturalHeight: figure.naturalHeight }, after: rendered && { width: rendered.width, height: rendered.height, naturalWidth: rendered.naturalWidth, naturalHeight: rendered.naturalHeight }, matches: !!rendered && ['width', 'height', 'naturalWidth', 'naturalHeight'].every(key => near(figure[key], rendered[key])) };
  });
  check(pair, topic, 'Every figure retains its natural and rendered dimensions', before.figures.length === after.figures.length && figures.every(f => f.matches), { beforeCount: before.figures.length, afterCount: after.figures.length, figures });
  if (before.preBlocks.length || after.preBlocks.length) {
    const code = before.preBlocks.map((pre, i) => ({ block: i + 1, differences: typographyDifferences(pre, after.preBlocks[i], ['paddingLeft', 'paddingRight', 'paddingTop', 'paddingBottom', 'background', 'border', 'whiteSpace', 'letterSpacing', 'overflowX', 'overflowY']) }));
    check(pair, topic, 'Every R code block retains its Flare font, spacing, padding, colours and border', before.preBlocks.length === after.preBlocks.length && code.every(c => c.differences.length === 0), { beforeCount: before.preBlocks.length, afterCount: after.preBlocks.length, code });
    const oldIndent = before.pre.x - before.dropdown.x, newIndent = after.pre.x - after.dropdown.x;
    check(pair, topic, 'R code body retains the Flare dropdown indent', near(oldIndent, newIndent), { before: oldIndent, after: newIndent });
  }
  const footer = typographyDifferences(before.footer, after.footer);
  check(pair, topic, 'Shared footer typography matches Flare', footer.length === 0, footer);
}
async function ready(page) {
  await page.waitForFunction(() => [...document.images].every(i => i.complete), { timeout: 20000 });
  if (await page.evaluate(() => !!window.MathJax?.startup?.promise)) await page.evaluate(() => MathJax.startup.promise);
}
async function measure(page) {
  return page.evaluate(() => {
    const container = document.getElementById('mc-main-content') || document.body;
    const style = e => {
      if (!e) return null; const s = getComputedStyle(e), r = e.getBoundingClientRect(), probe = document.createElement('canvas').getContext('2d');
      probe.font = `${s.fontStyle} ${s.fontWeight} ${s.fontSize} ${s.fontFamily}`;
      return { font: s.fontFamily, fontProbeWidth: probe.measureText('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789').width, fontWeight: s.fontWeight, fontStyle: s.fontStyle, fontSize: s.fontSize, lineHeight: s.lineHeight, color: s.color, background: s.backgroundColor, marginTop: s.marginTop, marginBottom: s.marginBottom, marginLeft: s.marginLeft, marginRight: s.marginRight, paddingLeft: s.paddingLeft, paddingRight: s.paddingRight, paddingTop: s.paddingTop, paddingBottom: s.paddingBottom, border: s.border, whiteSpace: s.whiteSpace, letterSpacing: s.letterSpacing, overflowX: s.overflowX, overflowY: s.overflowY, x: r.x, y: r.y, width: r.width, height: r.height };
    };
    const h1 = container.querySelector('h1'), paragraph = [...container.querySelectorAll('p')].find(p => p.textContent.trim().length > 35);
    const equations = [...container.querySelectorAll('img[src*="generatedimages/equations/"],.help-equation')].map(e => ({ ...style(e), src: e.getAttribute('src'), content: e.textContent.slice(0, 120), svg: style(e.querySelector('svg')), overflowWidth: e.scrollWidth, clientWidth: e.clientWidth }));
    const figures = [...container.querySelectorAll('img')].filter(e => !e.closest('.help-equation') && !e.src.includes('generatedimages/equations/') && !e.classList.contains('rlogo') && !e.classList.contains('MCDropDown_Image_Icon')).map(e => ({ src: e.getAttribute('src'), ...style(e), naturalWidth: e.naturalWidth, naturalHeight: e.naturalHeight }));
    const footer = [...document.querySelectorAll('p')].find(p => p.textContent.trim().startsWith('Copyright ©'));
    const sourceChildren = [...container.children].filter(e => !e.matches('nav,footer,script,style') && e.getBoundingClientRect().height > 0);
    const sourceContentHeight = h1 ? Math.max(...sourceChildren.map(e => e.getBoundingClientRect().bottom)) - h1.getBoundingClientRect().top : null;
    return { container: style(container), sourceContentHeight, body: style(document.body), h1: style(h1), h1Text: h1?.textContent, paragraph: style(paragraph), paragraphText: paragraph?.textContent, blankParagraph: style([...container.querySelectorAll('p')].find(p => !p.textContent.trim())), equations, figures, pre: style(container.querySelector('pre')), preBlocks: [...container.querySelectorAll('pre.rcode')].map(style), dropdown: style(container.querySelector('details,.dropDownrcode')), footer: style(footer), horizontalOverflow: document.documentElement.scrollWidth > innerWidth };
  });
}
async function contentScreenshot(page, file) {
  const clip = await page.evaluate(() => { const r = document.querySelector('h1').getBoundingClientRect(); return { x: r.x + scrollX, y: r.y + scrollY, width: 900, height: 850 }; });
  await page.screenshot({ path: file, clip });
}
(async () => {
  let browser; const results = [], pageErrors = [], externalBlocked = new Set();
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1400, height: 1100 } });
    await context.route('**/*', route => { const u = route.request().url(); if (u.startsWith(origin + '/') || /^(data|blob):/.test(u)) return route.continue(); externalBlocked.add(u); return route.abort(); });
    for (const [baseline, output] of pairs) for (const topic of topics) {
      const comparisons = [];
      for (const target of [baseline, output]) {
        const page = await context.newPage(); page.on('pageerror', e => pageErrors.push({ target, topic, message: e.message }));
        await page.goto(`${origin}/${target}/${topic}${target===output?'?content=1':''}`, { waitUntil: 'load' }); await ready(page);
        const natural = await measure(page);
        await page.evaluate(() => { const container = document.getElementById('mc-main-content') || document.body; container.style.width = '900px'; container.style.maxWidth = 'none'; container.style.boxSizing = 'content-box'; if (container === document.body) container.style.margin = '0'; });
        const matched = await measure(page), name = `${baseline.toLowerCase()}-${topic.replaceAll('/', '-').replace('.htm', '')}-${target === baseline ? 'flare' : 'editor'}`;
        await contentScreenshot(page, path.join(evidence, `visual-fidelity-${name}.png`));
        if (matched.pre) {
          if (target === baseline) {
            const dropdown = page.locator('a.dropDownHotspotrcode').first();
            if (await dropdown.count()) {
              await dropdown.click();
              await page.waitForFunction(() => { const pre = document.querySelector('pre'), body = pre?.closest('.dropDownBody'); return body && body.getBoundingClientRect().height >= pre.getBoundingClientRect().height; });
            }
          }
          else { const details = page.locator('details').filter({ has: page.locator('pre') }).first(); if (await details.count()) await details.locator('summary').click(); }
          const afterOpening = await measure(page); matched.pre = afterOpening.pre; matched.preBlocks = afterOpening.preBlocks; matched.dropdown = afterOpening.dropdown;
          const pre = page.locator('pre').first(); await pre.scrollIntoViewIfNeeded(); await page.screenshot({ path: path.join(evidence, `visual-fidelity-${name}-rcode.png`) });
        }
        comparisons.push({ target, natural, matched }); await page.close();
      }
      results.push({ baseline, output, topic, comparisons });
      verifyPair([baseline, output], topic, comparisons[0].matched, comparisons[1].matched);
      console.log(`Compared ${baseline} -> ${output}: ${topic}`);
    }
    const passed = checks.every(c => c.passed) && pageErrors.length === 0;
    const report = { passed, assessedAtUtc: new Date().toISOString(), method: 'Actual Edge rendering; 1400px viewport and separate 900px topic content boxes; exact typography and figure checks, not pixel-equivalence or whole-page-height assertions', checks, results, externalBlocked: [...externalBlocked], pageErrors };
    fs.writeFileSync(path.join(evidence, 'visual-fidelity-checks.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ passed, comparisons: results.length, checks: checks.length, failures: checks.filter(c => !c.passed), pageErrors, externalBlocked: [...externalBlocked] }, null, 2));
    if (!passed) process.exitCode = 1;
  } finally { if (browser) await browser.close(); server.close(); }
})().catch(e => { console.error(e); server.close(); process.exitCode = 1; });
