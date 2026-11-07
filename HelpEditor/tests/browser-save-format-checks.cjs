// Run against a dedicated browser host, for example:
// node tests/browser-save-format-checks.cjs http://127.0.0.1:5849 evidence/browser-save-format-checks-mac
// Start that host with --workspace pointing to the same disposable folder.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('./browser-runtime.cjs');

const [address, workspaceArgument] = process.argv.slice(2);
if (!address || !workspaceArgument) throw new Error('Supply the browser host URL and its disposable save-format-checks workspace.');
const origin = new URL(address).origin;
if (new URL(origin).hostname !== '127.0.0.1') throw new Error('Use the loopback browser test host.');
const workspace = fs.realpathSync(path.resolve(workspaceArgument));
if (!path.basename(workspace).includes('save-format-checks')) throw new Error('Use a dedicated save-format-checks workspace.');
if (fs.readFileSync(path.join(workspace, '.statsdirect-browser-draft'), 'utf8').trim() !== '1') {
  throw new Error('The folder is not a browser draft workspace.');
}

const topicPath = 'parametric_methods/paired_t.htm';
const topicFile = path.join(workspace, 'HelpSource', 'Content', ...topicPath.split('/'));
const originalBytes = fs.readFileSync(topicFile);
const bom = Buffer.from([0xef, 0xbb, 0xbf]);
const checks = [];
const browserErrors = [];
let browser;
let page;

function check(name, condition, detail) {
  checks.push({ name, passed: !!condition, ...(detail ? { detail } : {}) });
  assert.ok(condition, name);
  console.log('PASS: ' + name);
}

function fixture(endings, trailingNewline = true) {
  const lines = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<html xmlns:sdh="https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd">',
    '<head><title>Browser save format fixture</title></head>',
    '<body>',
    '<h1>Format preservation</h1>',
    '<p id="change">Original café α &amp; β.</p>',
    '<p>Unicode paragraph\u2028separator stays in the text.</p>',
    '<pre>first R line',
    'second R line</pre>',
    '</body>',
    '</html>'
  ];
  return lines.map((line, index) => line + (index < lines.length - 1 || trailingNewline ? endings[index % endings.length] : '')).join('');
}

function encoded(source, withBom) {
  const content = Buffer.from(source, 'utf8');
  return withBom ? Buffer.concat([bom, content]) : content;
}

function normalized(source) {
  return source.replace(/\r\n|\r/g, '\n');
}

async function api(route, data) {
  const response = await fetch(origin + route, data === undefined ? {} : {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Origin: origin },
    body: JSON.stringify(data)
  });
  return { status: response.status, body: await response.json() };
}

async function openFixture(source, withBom) {
  fs.writeFileSync(topicFile, encoded(source, withBom));
  await page.goto(origin + '/');
  await page.waitForFunction(() => {
    const frame = document.getElementById('editor-frame');
    return frame?.contentWindow?.HelpEditor?.getState().path === 'parametric_methods/paired_t.htm';
  });
  const sourceTab = page.frameLocator('#editor-frame').getByRole('tab', { name: 'HTML source', exact: true });
  await sourceTab.click();
  const textarea = sourceEditor();
  assert.equal(await textarea.inputValue(), normalized(source));
  return textarea;
}

function sourceEditor() {
  return page.frameLocator('#editor-frame').getByRole('textbox', { name: 'Complete XHTML source', exact: true });
}

async function save(textarea, useShortcut = false, expectedStatus = 200) {
  const responsePromise = page.waitForResponse(response => response.url() === origin + '/api/topic'
    && response.request().method() === 'POST');
  if (useShortcut) await textarea.press(process.platform === 'darwin' ? 'Meta+s' : 'Control+s');
  else await page.getByRole('button', { name: 'Save page', exact: true }).click();
  const response = await responsePromise;
  assert.equal(response.status(), expectedStatus);
  if (expectedStatus === 200) {
    await page.waitForFunction(() => document.getElementById('page-state').textContent === 'Saved in scratch workspace');
  } else {
    await page.getByRole('alert').waitFor({ state: 'visible' });
  }
  return response.json();
}

async function checkFixture(name, source, withBom, useShortcut, edit = value => value.replace('Original', 'Edited')) {
  const textarea = await openFixture(source, withBom);
  const edited = edit(source);
  await textarea.fill(normalized(edited));
  const caret = normalized(edited).indexOf('café') + 2;
  await textarea.evaluate((element, position) => element.setSelectionRange(position, position), caret);
  await save(textarea, useShortcut);
  check(name + ' keeps the original newline bytes and UTF-8 BOM policy on edit', fs.readFileSync(topicFile).equals(encoded(edited, withBom)));
  const selection = await textarea.evaluate(element => ({ start: element.selectionStart, end: element.selectionEnd }));
  check(name + ' keeps the source caret after the save response', selection.start === caret && selection.end === caret, selection);

  // Filling the textarea normalizes its value again. A no-op save must not rewrite the disk file.
  await textarea.fill(normalized(edited));
  const before = fs.statSync(topicFile).mtimeMs;
  await page.getByRole('button', { name: 'Save page', exact: true }).click();
  await page.waitForFunction(() => document.getElementById('page-state').textContent === 'Saved in scratch workspace');
  check(name + ' leaves a no-op save byte-for-byte intact', fs.readFileSync(topicFile).equals(encoded(edited, withBom)) && fs.statSync(topicFile).mtimeMs === before);

  await textarea.fill(normalized(source));
  await save(textarea, !useShortcut);
  check(name + ' restores the original file bytes after reverting the text', fs.readFileSync(topicFile).equals(encoded(source, withBom)));
}

async function checkFormatConflict(name, change) {
  const source = fixture(['\r\n']);
  const textarea = await openFixture(source, true);
  const initial = await api('/api/topic?' + new URLSearchParams({ path: topicPath }));
  const external = change(encoded(source, true));
  fs.writeFileSync(topicFile, external);
  const changed = await api('/api/topic?' + new URLSearchParams({ path: topicPath }));
  check(name + ' changes the byte revision', initial.body.revision !== changed.body.revision);
  const attempted = normalized(source.replace('Original', 'Stale draft'));
  await textarea.fill(attempted);
  await save(textarea, false, 409);
  check(name + ' rejects the stale save and preserves both disk and editor', fs.readFileSync(topicFile).equals(external)
    && await textarea.inputValue() === attempted);
}

async function checkInFlightSave() {
  const source = fixture(['\r\n']);
  const textarea = await openFixture(source, true);
  const first = source.replace('Original', 'First saved');
  const later = source.replace('Original', 'Later unsaved');
  let releaseResponse;
  let notifySaved;
  let responseTimeout;
  const held = new Promise(resolve => { releaseResponse = resolve; });
  const serverSaved = new Promise((resolve, reject) => {
    notifySaved = resolve;
    responseTimeout = setTimeout(() => reject(new Error('The held browser save did not reach the server.')), 15000);
  });
  await page.route(origin + '/api/topic', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    const response = await route.fetch();
    notifySaved();
    await held;
    await route.fulfill({ response });
  }, { times: 1 });
  await textarea.fill(normalized(first));
  await page.getByRole('button', { name: 'Save page', exact: true }).click();
  try { await serverSaved; }
  finally { clearTimeout(responseTimeout); }
  await textarea.fill(normalized(later));
  releaseResponse();
  await page.waitForFunction(() => document.getElementById('status').textContent.includes('later edits'));
  check('A delayed authoritative save retains the newer source draft', await textarea.inputValue() === normalized(later)
    && fs.readFileSync(topicFile).equals(encoded(first, true)));
  await save(textarea, true);
  check('The following save uses the returned byte revision and original format', fs.readFileSync(topicFile).equals(encoded(later, true)));
}

async function checkVisualSave() {
  const source = fixture(['\r\n']);
  await openFixture(source, true);
  const frame = page.frameLocator('#editor-frame');
  await frame.getByRole('tab', { name: 'Edit page', exact: true }).click();
  await frame.locator('#visual-editor #change').fill('Visual edit café α & β.');
  const responsePromise = page.waitForResponse(response => response.url() === origin + '/api/topic'
    && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Save page', exact: true }).click();
  assert.equal((await responsePromise).status(), 200);
  await page.waitForFunction(() => document.getElementById('page-state').textContent === 'Saved in scratch workspace');
  const bytes = fs.readFileSync(topicFile);
  const text = bytes.subarray(3).toString('utf8');
  check('Visual editing preserves the UTF-8 BOM and CRLF line endings', bytes.subarray(0, 3).equals(bom)
    && text.includes('Visual edit café') && text.includes('\r\n') && !/(?<!\r)\n|\r(?!\n)/.test(text));
}

(async () => {
  try {
    const executable = process.env.STATSDIRECT_CHROME_PATH;
    const options = { headless: true };
    if (executable) options.executablePath = executable;
    else if (process.platform === 'darwin') options.executablePath = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
    else options.channel = 'msedge';
    browser = await chromium.launch(options);
    page = await browser.newPage({ viewport: { width: 1280, height: 850 } });
    page.on('pageerror', error => browserErrors.push(error.message));
    page.on('dialog', dialog => dialog.accept());
    for (const [index, [name, endings, withBom, trailing]] of [
      ['CRLF with BOM', ['\r\n'], true, true],
      ['CRLF without BOM', ['\r\n'], false, false],
      ['LF with BOM', ['\n'], true, false],
      ['LF without BOM', ['\n'], false, true],
      ['CR with BOM', ['\r'], true, true],
      ['CR without BOM', ['\r'], false, false],
      ['Mixed endings with BOM', ['\r\n', '\n', '\r'], true, true],
      ['Mixed endings without BOM', ['\n', '\r\n', '\r'], false, false]
    ].entries()) {
      await checkFixture(name, fixture(endings, trailing), withBom, index % 2 === 1);
    }
    await checkFixture('Mixed endings with separated edits and insertion', fixture(['\r\n', '\n', '\r']), true, false,
      source => source.replace('Original', 'Edited').replace('<pre>', '<p>Added line.</p>\r\n<pre>'));
    await checkFixture('Mixed endings around repeated lines', '<html>\r\n<body>\n<p>x</p>\r<p>x</p>\r\n</body>\n</html>', true, false,
      source => source.replace('<p>x</p>\r\n', '<p>new</p>\r\n<p>x</p>\r\n'));
    await checkFormatConflict('An external BOM-only change', bytes => bytes.subarray(3));
    await checkFormatConflict('An external newline-only change', bytes => Buffer.from(bytes.toString('utf8').replace(/\r\n/g, '\n'), 'utf8'));
    await checkInFlightSave();
    await checkVisualSave();
    check('The browser reported no unhandled errors', browserErrors.length === 0, browserErrors);
  } catch (error) {
    checks.push({ name: 'Browser save checks completed', passed: false, detail: error.stack || String(error) });
    process.exitCode = 1;
    console.error(error);
  } finally {
    fs.writeFileSync(topicFile, originalBytes);
    if (browser) await browser.close();
    const result = { checkedAt: new Date().toISOString(), host: origin, platform: process.platform,
      browser: 'Chromium', checks, browserErrors, passed: checks.every(item => item.passed) };
    fs.writeFileSync(path.join(workspace, 'save-format-checks.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify({ passed: result.passed, checks: checks.length }));
  }
})();
