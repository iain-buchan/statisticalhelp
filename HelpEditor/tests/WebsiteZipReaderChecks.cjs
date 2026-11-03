// Independent consumer test: extract an already-created website ZIP, then use
// only file:// URLs. This never builds, exports, publishes or edits help source.
const fs = require('node:fs'), path = require('node:path'), url = require('node:url');
const cp = require('node:child_process'), crypto = require('node:crypto'), vm = require('node:vm');
const { chromium } = require('./browser-runtime.cjs');
const args = process.argv.slice(2);
const option = name => { const index = args.indexOf(name); return index < 0 ? null : args[index + 1]; };
const zipPath = option('--zip');
if (!zipPath) throw Error('Use --zip <existing website ZIP>, optionally --expect <expectations JSON>. No build is run.');
const archive = path.resolve(zipPath), root = path.resolve(__dirname, '..');
const evidence = path.resolve(option('--evidence') || path.join(root, 'evidence/WebsiteZipReader-' + crypto.randomUUID().replaceAll('-', '')));
const extracted = path.join(evidence, 'Extracted Website');
const expected = option('--expect') ? JSON.parse(fs.readFileSync(path.resolve(option('--expect')), 'utf8')) : {};
const sourceContent = option('--source') ? path.resolve(option('--source')) : null;
fs.mkdirSync(evidence, { recursive: true });
const checks = [], pages = [], errors = [], failedRequests = [], external = [];
function record(name, passed, detail) { checks.push({ name, passed: !!passed, ...(detail === undefined ? {} : { detail }) }); }
function files(directory) { return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => entry.isDirectory() ? files(path.join(directory, entry.name)) : [path.join(directory, entry.name)]); }
const extractionScript = String.raw`
import json,pathlib,stat,sys,zipfile
archive=pathlib.Path(sys.argv[1]); destination=pathlib.Path(sys.argv[2])
if destination.exists(): raise RuntimeError('Extraction destination must be new; no existing files are replaced.')
with zipfile.ZipFile(archive) as z:
 entries=z.infolist(); seen=set(); output=[]
 for entry in entries:
  name=entry.filename; parts=name.split('/')
  if not name or chr(92) in name or name.startswith('/') or ':' in name or any(part in ('.','..') for part in parts) or any(part.endswith((' ','.')) for part in parts if part) or any(not part for part in parts[:-1]): raise RuntimeError('Unsafe archive path: '+name)
  key=name.casefold()
  if key in seen: raise RuntimeError('Duplicate archive path: '+name)
  seen.add(key)
  if stat.S_ISLNK(entry.external_attr>>16): raise RuntimeError('Linked archive entry: '+name)
  output.append({'path':name,'bytes':entry.file_size,'compressedBytes':entry.compress_size,'directory':entry.is_dir()})
 destination.mkdir()
 z.extractall(destination)
 print(json.dumps(output))
`;
async function currentTopic(page) {
  await page.waitForFunction(() => !!window.StatsDirectHelp);
  if (await page.locator('article#topic').count()) return page;
  return (await page.locator('#topic').elementHandle()).contentFrame();
}
async function waitReady(frame) {
  await frame.waitForFunction(() => !!window.MathJax?.startup?.promise, {}, { timeout: 20000 });
  await frame.evaluate(() => MathJax.startup.promise);
  await frame.waitForFunction(() => [...document.images].every(image => image.complete));
}
(async () => {
  let browser, entries = [], searchDocuments = 0, zipDigest;
  try {
    zipDigest = crypto.createHash('sha256').update(fs.readFileSync(archive)).digest('hex');
    const extraction = cp.spawnSync('python', ['-c', extractionScript, archive, extracted], { encoding: 'utf8', windowsHide: true, timeout: 120000, maxBuffer: 10 * 1024 * 1024 });
    if (extraction.status !== 0) throw Error(extraction.stderr || extraction.stdout);
    entries = JSON.parse(extraction.stdout); const names = entries.filter(entry => !entry.directory).map(entry => entry.path);
    fs.writeFileSync(path.join(evidence, 'archive-entries.json'), JSON.stringify(entries, null, 2));
    record('Archive extracts safely with flat-root reader entry points', ['index.html', 'contents.htm', 'contents-data.js', 'search-index.js', 'help-viewer.js', 'help-viewer.css'].every(name => names.includes(name)), { entries: names.length });
    const leakedNames = names.filter(name => /(^|\/)(?:\.git|\.help-editor-recovery|\.editor-profile|\.app-help-preview-profile|\.preview-profile|HelpSource|Project|Schema|secrets|Workspace)(\/|$)|\.(?:flprj|fltoc|fltar|csproj|pfx|pdb|sln|xsd)$|(^|\/)(?:editor\.js|editor\.css|config\.toml|auth\.json|\.statsdirect-help-output\.json|\.statsdirect-help-editor\.json)$/i.test(name));
    record('Archive contains reader files without project credentials recovery state or editor UI', leakedNames.length === 0, leakedNames);
    const topicFiles = files(extracted).filter(file => /\.htm$/i.test(file));
    record('Archive contains all 404 generated Website topics', topicFiles.length === (expected.topicCount || 404), { topics: topicFiles.length });
    const sandbox = { window: {} }; vm.runInNewContext(fs.readFileSync(path.join(extracted, 'search-index.js'), 'utf8'), sandbox, { timeout: 5000 });
    const search = sandbox.window.StatsDirectSearch; searchDocuments = search.documents.length;
    record('Offline search index contains all 403 searchable topics', searchDocuments === (expected.searchDocumentCount || 403), { searchDocuments });
    const privateMatches = [];
    for (const file of files(extracted).filter(file => /\.(?:html?|js|css|json|xml)$/i.test(file))) {
      const text = fs.readFileSync(file, 'utf8');
      if (/C:\\(?:Develop|Users)\\|help-(?:editor|project|preview)\.invalid|\.help-editor-recovery|__statsdirect_editor_asset/i.test(text)) privateMatches.push(path.relative(extracted, file));
      if ((expected.absentEverywhere || []).some(marker => text.includes(marker))) privateMatches.push(path.relative(extracted, file) + ': excluded fixture marker');
    }
    record('Extracted reader text contains no local editor paths private origins or excluded markers', privateMatches.length === 0, privateMatches);
    if (fs.existsSync(path.join(extracted, 'build-info.json'))) {
      const info = JSON.parse(fs.readFileSync(path.join(extracted, 'build-info.json'), 'utf8'));
      record('Export records a fresh Public target with the full topic and search counts', info.Target === 'Public' && info.TopicCount === (expected.topicCount || 404) && info.SearchDocumentCount === (expected.searchDocumentCount || 403) && !info.Warnings.length, info);
    }
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
    await context.route('**/*', route => {
      const value = route.request().url();
      if (/^(file|data|blob):/.test(value) || value === 'about:blank') return route.continue();
      external.push(value); return route.abort();
    });
    let cursor = 0;
    await Promise.all(Array.from({ length: 2 }, async () => {
      const page = await context.newPage(); const localErrors = [];
      page.on('pageerror', error => { localErrors.push(error.message); errors.push(error.message); });
      page.on('requestfailed', request => { if (request.failure()?.errorText !== 'net::ERR_ABORTED') failedRequests.push({ url: request.url(), error: request.failure()?.errorText }); });
      while (cursor < topicFiles.length) {
        const file = topicFiles[cursor++], relative = path.relative(extracted, file).replaceAll('\\', '/'); localErrors.length = 0;
        try {
          await page.goto(url.pathToFileURL(file).href + '?content=1', { waitUntil: 'load' }); await waitReady(page);
          let reference = null;
          if (expected.topic === relative && expected.draftSourceFile) reference = fs.readFileSync(path.resolve(expected.draftSourceFile), 'utf8');
          else if (sourceContent && expected.topic !== relative) reference = fs.readFileSync(path.join(sourceContent, relative), 'utf8');
          const state = await page.evaluate(reference => {
            const topic = document.querySelector('#help-topic-body') || document.body;
            const equations = [...topic.querySelectorAll('.help-equation')];
            let sourcePreMatches = null;
            if (reference) {
              const source = new DOMParser().parseFromString(reference, 'application/xml');
              if (source.querySelector('parsererror')) throw Error('Reference source could not be parsed.');
              const authoring = ['https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd', 'http://www.madcapsoftware.com/Schemas/MadCap.xsd'];
              const excluded = element => { for (let current = element; current; current = current.parentElement) if (authoring.some(uri => (current.getAttributeNS(uri, 'conditions') || '').split(/[;,\s]+/).includes('Primary.SWHELP'))) return true; return false; };
              const normalize = text => text.replace(/\r\n?/g, '\n');
              sourcePreMatches = JSON.stringify([...source.querySelectorAll('pre')].filter(element => !excluded(element)).map(element => normalize(element.textContent))) === JSON.stringify([...topic.querySelectorAll('pre')].map(element => normalize(element.textContent)));
            }
            return {
              equations: equations.length,
              renderedEquations: equations.filter(equation => { const image = equation.querySelector('img.help-equation-original'); return !!equation.querySelector('mjx-container svg') || image?.complete && image.naturalWidth > 0; }).length,
              mathErrors: topic.querySelectorAll('[data-mml-node="merror"],mjx-merror,.MathJax_Error').length,
              missingImages: [...document.images].filter(image => image.naturalWidth === 0).map(image => image.getAttribute('src')),
              rDropdowns: topic.querySelectorAll('details.rcode').length,
              preBlocks: topic.querySelectorAll('pre').length,
              sourcePreMatches,
              remainingAuthoringTags: [...topic.querySelectorAll('*')].filter(element => /^(?:SDH|MADCAP):/i.test(element.tagName)).map(element => element.tagName),
              appearanceControls: [...document.querySelectorAll('select,[role="combobox"]')].filter(element => /appearance|theme|skin|colour scheme/i.test((element.id || '') + ' ' + (element.getAttribute('aria-label') || '') + ' ' + (element.getAttribute('title') || ''))).map(element => element.outerHTML)
            };
          }, reference);
          pages.push({ topic: relative, ...state, errors: [...localErrors], passed: state.equations === state.renderedEquations && !state.mathErrors && !state.missingImages.length && !state.remainingAuthoringTags.length && !state.appearanceControls.length && state.sourcePreMatches !== false && !localErrors.length });
        } catch (error) { pages.push({ topic: relative, passed: false, error: error.stack || String(error) }); }
      }
      await page.close();
    }));
    const totals = { pages: pages.length, equations: pages.reduce((sum, row) => sum + (row.equations || 0), 0), renderedEquations: pages.reduce((sum, row) => sum + (row.renderedEquations || 0), 0), rDropdowns: pages.reduce((sum, row) => sum + (row.rDropdowns || 0), 0), preBlocks: pages.reduce((sum, row) => sum + (row.preBlocks || 0), 0), exactSourcePreComparisons: pages.filter(row => row.sourcePreMatches === true).length };
    record('Every extracted Website topic renders offline with complete images mathematics and no authoring controls', pages.length === topicFiles.length && pages.every(row => row.passed) && (expected.equations ? totals.equations === expected.equations : totals.equations >= (expected.minimumEquations || 258)), { ...totals, failures: pages.filter(row => !row.passed) });
    const viewer = await context.newPage(); viewer.on('pageerror', error => errors.push(error.message));
    viewer.on('requestfailed', request => { if (request.failure()?.errorText !== 'net::ERR_ABORTED') failedRequests.push({ url: request.url(), error: request.failure()?.errorText }); });
    await viewer.goto(url.pathToFileURL(path.join(extracted, 'index.html')).href); await viewer.waitForFunction(() => !!window.StatsDirectHelp && !!window.StatsDirectSearch);
    record('Double-click entry point opens a complete local Contents viewer', viewer.url().startsWith('file:') && await viewer.locator('#contents a').count() >= 260);
    await viewer.evaluate(() => { for (const details of document.querySelectorAll('#contents details')) details.open = true; });
    await viewer.locator('#contents a[data-link="rates/incidence_and_prevalence.htm"]').click(); let topic = await currentTopic(viewer); await topic.locator('h1').filter({ hasText: 'Incidence and Prevalence' }).waitFor(); await waitReady(topic);
    record('Contents navigation opens local equations', await topic.locator('.help-equation').count() === 2);
    await viewer.fill('#search', 'box whisker'); await viewer.locator('#search').press('Enter'); await viewer.locator('#results a[data-link="graphics/box_whisker.htm"]').waitFor(); const searchUrl = viewer.url();
    await viewer.locator('#results a[data-link="graphics/box_whisker.htm"]').click(); topic = await currentTopic(viewer); await topic.locator('h1').filter({ hasText: 'Box and Whisker Plot' }).waitFor(); await waitReady(topic);
    const graphics = await topic.evaluate(() => ({ svg: !!document.querySelector('img[src$="boxwhisker.svg"]'), png: !!document.querySelector('img[src$="boxwhisker.png"]'), loaded: [...document.images].every(image => image.complete && image.naturalWidth > 0) }));
    record('Search opens Website SVG graphics with Desktop-only artwork excluded', graphics.svg && !graphics.png && graphics.loaded, graphics);
    const dropdown = topic.locator('details').filter({ has: topic.locator('pre') }).first(); await dropdown.locator('summary').click();
    record('Extracted R-code dropdown expands and retains real code', await dropdown.evaluate(element => element.open) && await dropdown.locator('pre').isVisible() && (await dropdown.locator('pre').textContent()).includes('boxplot('));
    await viewer.screenshot({ path: path.join(evidence, 'offline-reader-graphics-rcode.png') });
    await viewer.goBack(); await viewer.waitForFunction(() => !!window.StatsDirectHelp && !!window.StatsDirectSearch);
    record('Browser Back restores the previous local search navigation URL', viewer.url() === searchUrl, { expected: searchUrl, actual: viewer.url() });
    if (expected.topic) {
      await viewer.goto(url.pathToFileURL(path.join(extracted, expected.topic)).href); topic = await currentTopic(viewer); await waitReady(topic);
      const content = await topic.evaluate(() => (document.querySelector('#help-topic-body') || document.body).textContent);
      record('Exported saved and unsaved fixture text is current and Website conditions apply', (expected.presentText || []).every(marker => content.includes(marker)) && (expected.absentText || []).every(marker => !content.includes(marker)), { topic: expected.topic });
      if (expected.title) {
        const exportedTitle = await viewer.title(), searchTitle = search.documents.find(document => document.url === expected.topic)?.title;
        record('Unsaved title is current in the exported topic and search document', exportedTitle.includes(expected.title) && searchTitle === expected.title, { exportedTitle, searchTitle });
      }
      if (expected.preText) {
        const codeBlocks = await topic.locator('pre').allTextContents();
        record('Unsaved exported R code retains every fixture character', expected.preText.every(text => codeBlocks.some(block => block.replace(/\r\n?/g, '\n') === text.replace(/\r\n?/g, '\n'))));
      }
      if (expected.searchQuery) {
        const matches = await viewer.evaluate(query => StatsDirectHelp.search(query).map(result => result.url), expected.searchQuery);
        record('Fresh unsaved override text is searchable in the exported reader', matches.includes(expected.topic), { query: expected.searchQuery, matches });
      }
      if (expected.anchor) {
        await viewer.goto(url.pathToFileURL(path.join(extracted, expected.topic)).href + '#' + encodeURIComponent(expected.anchor)); topic = await currentTopic(viewer); await waitReady(topic);
        record('Extracted deep-link anchors retain exact case and local URL navigation', await topic.evaluate(anchor => !!document.getElementById(anchor) || !!document.querySelector('a[name="' + CSS.escape(anchor) + '"]'), expected.anchor) && decodeURIComponent(new URL(viewer.url()).hash.slice(1)) === expected.anchor);
      }
      await viewer.screenshot({ path: path.join(evidence, 'offline-reader-current-draft.png') });
    }
    await viewer.reload(); await viewer.waitForFunction(() => !!window.StatsDirectHelp && !!window.StatsDirectSearch); topic = await currentTopic(viewer); await waitReady(topic);
    record('Reloaded extracted reader remains functional', viewer.url().startsWith('file:') && await topic.locator('h1').count() > 0);
    record('Reader requires no server external network or failed resources', external.length === 0 && errors.length === 0 && failedRequests.length === 0, { external, errors, failedRequests });
    await context.close();
  } catch (error) { record('Independent ZIP reader checks completed', false, error.stack || String(error)); }
  finally {
    if (browser) await browser.close();
    const report = { checkedAtUtc: new Date().toISOString(), passed: checks.length > 0 && checks.every(check => check.passed), archive, archiveSha256: zipDigest, extracted, expected, entries: entries.length, searchDocuments, scheme: 'file://', noBuildOrSourceWrites: true, checks, pages, errors, failedRequests, external };
    fs.writeFileSync(path.join(evidence, 'reader-checks.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ passed: report.passed, evidence, checks: checks.length, pages: pages.length, failures: checks.filter(check => !check.passed) }, null, 2)); if (!report.passed) process.exitCode = 1;
  }
})();
