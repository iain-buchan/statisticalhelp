// Additional authoring workflows use in-memory topics, a local HTTP server and
// a stub native bridge. No user source, clipboard or live service is modified.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require('./browser-runtime.cjs');
const root = path.resolve(__dirname, '..');
const web = path.join(root, 'src/StatsDirectHelpEditor.App/Web');
const evidence = path.join(root, 'evidence/OvernightBrowser');
fs.mkdirSync(evidence, { recursive: true });
const SDH = 'https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd';
const MC = 'http://www.madcapsoftware.com/Schemas/MadCap.xsd';
const checks = [], errors = [], external = [];
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.woff': 'font/woff', '.woff2': 'font/woff2', '.json': 'application/json', '.svg': 'image/svg+xml' };
const server = http.createServer((req, res) => {
  const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  if (pathname.startsWith('/project/') && pathname.endsWith('.svg')) {
    res.setHeader('Content-Type', 'image/svg+xml');
    res.end('<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12"><rect width="12" height="12" fill="blue"/></svg>');
    return;
  }
  if (pathname.startsWith('/generated/')) {
    res.setHeader('Content-Type', 'text/html');
    res.end('<!doctype html><html><body><h1 id="start">Generated fixture</h1><details id="reader"><summary>R example</summary><pre>x &lt;- 1</pre></details>' + '<p>Reader text</p>'.repeat(120) + '</body></html>');
    return;
  }
  const file = path.resolve(web, pathname === '/' ? 'index.html' : pathname.slice(1));
  if (!file.startsWith(web + path.sep)) { res.writeHead(403).end(); return; }
  try { res.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream'); res.end(fs.readFileSync(file)); }
  catch { res.writeHead(404).end(); }
});
function xml(body, namespace = SDH, prefix = 'sdh') {
  return '<?xml version="1.0" encoding="utf-8"?>\n<html xmlns:' + prefix + '="' + namespace + '" xmlns:qa="urn:overnight:qa"><head><title>Isolated authoring fixture</title><meta name="preserve" content="α &amp; β"/></head><body>' + body + '</body></html>';
}
const code = 'x <- c(1, 2)\nif (x[1] < 3 && x[2] > 0) print("Café α 🧬")';
function body(prefix = 'sdh') {
  return '<h1 id="heading">Isolated authoring</h1><p id="prose">Bold and italic text α.</p>' +
    '<p id="list-one" qa:flag="keep" ' + prefix + ':conditions="Primary.SWHELP">List first.</p><p id="list-two">List second.</p>' +
    '<p id="tail">Final paragraph.</p><p><' + prefix + ':equation>$x+1$</' + prefix + ':equation></p>' +
    '<' + prefix + ':dropDown class="rcode"><' + prefix + ':dropDownHead><' + prefix + ':dropDownHotspot>R code</' + prefix + ':dropDownHotspot></' + prefix + ':dropDownHead><' + prefix + ':dropDownBody><pre><![CDATA[' + code + ']]></pre></' + prefix + ':dropDownBody></' + prefix + ':dropDown>';
}
async function select(page, selector) {
  await page.evaluate(selector => {
    const element = document.querySelector(selector), visual = document.getElementById('visual-editor');
    visual.focus(); const range = document.createRange(); range.selectNodeContents(element);
    getSelection().removeAllRanges(); getSelection().addRange(range); document.dispatchEvent(new Event('selectionchange'));
  }, selector);
}
async function run(name, page, task) {
  try {
    const detail = await task();
    const passed = detail === true || detail && Object.values(detail).every(value => value === true);
    checks.push({ name, passed: !!passed, detail });
    if (!passed) {
      const stem = path.join(evidence, name.replace(/[^a-z0-9]+/gi, '-').slice(0, 100));
      await page.screenshot({ path: stem + '.png' });
      fs.writeFileSync(stem + '.xhtml', await page.evaluate(() => HelpEditor.getSource()).catch(() => 'Unavailable: malformed draft'));
    }
  } catch (error) {
    checks.push({ name, passed: false, detail: error.stack || String(error) });
    await page.screenshot({ path: path.join(evidence, name.replace(/[^a-z0-9]+/gi, '-').slice(0, 100) + '.png') }).catch(() => {});
  }
}
(async () => {
  let browser;
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = 'http://127.0.0.1:' + server.address().port;
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
    await context.route('**/*', route => {
      const url = route.request().url();
      if (url.startsWith(origin + '/') || /^(data|blob):/.test(url)) return route.continue();
      external.push(url); return route.abort();
    });
    async function open() {
      const page = await context.newPage(); page.setDefaultTimeout(8000);
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(() => { window.hostMessages = []; window.chrome ||= {}; window.chrome.webview = { postMessage: message => hostMessages.push(message) }; });
      await page.goto(origin + '/'); await page.waitForFunction(() => !!window.HelpEditor); return page;
    }
    const page = await open();
    const payload = { path: 'chapters/editor.htm', title: 'Authoring fixture', baseUrl: origin + '/project/chapters/editor.htm', source: xml(body()) };
    const load = async (source = payload.source) => page.evaluate(payload => { HelpEditor.setWorkspaceMode(false); HelpEditor.setUnifiedViews(false); HelpEditor.load(payload); }, { ...payload, source });

    await run('Reviewed text preserves a real scrolled formatted selection', page, async () => {
      const source = xml(Array.from({ length: 150 }, (_, i) => '<p id="line-' + i + '">Paragraph ' + i + ' with original wording.</p>').join(''));
      await load(source);
      return page.evaluate(() => {
        const visual = document.getElementById('visual-editor'), panel = document.getElementById('visual-panel'), paragraph = document.getElementById('line-120');
        visual.focus(); const range = document.createRange(); range.selectNodeContents(paragraph); getSelection().removeAllRanges(); getSelection().addRange(range); paragraph.scrollIntoView({ block: 'center' });
        const before = panel.scrollTop, selected = getSelection().toString(), expected = HelpEditor.getSource();
        HelpEditor.applyReviewedSource('chapters/editor.htm', expected, expected.replace('Paragraph 1 with original wording.', 'Paragraph 1 with clearer wording.'));
        return { exercisedScroll: before > 1000, scroll: panel.scrollTop === before, selection: getSelection().toString() === selected, dirty: HelpEditor.getState().dirty };
      });
    });

    await run('Reviewed-source guards leave draft and history unchanged', page, async () => {
      await load();
      return page.evaluate(() => {
        const expected = HelpEditor.getSource(), index = HelpEditor.getState().historyIndex;
        const attempts = [['other.htm', expected, expected], ['chapters/editor.htm', expected + ' stale', expected], ['chapters/editor.htm', expected, '<html><body>Broken']];
        let rejected = 0; for (const args of attempts) try { HelpEditor.applyReviewedSource(...args); } catch { rejected++; }
        const unchanged = HelpEditor.getSource() === expected && HelpEditor.getState().historyIndex === index && !HelpEditor.getState().dirty;
        HelpEditor.openEquation(); let modal = false; try { HelpEditor.applyReviewedSource('chapters/editor.htm', expected, expected); } catch { modal = true; } document.getElementById('equation-cancel').click();
        HelpEditor.applyReviewedSource('chapters/editor.htm', expected, expected);
        return { rejected: rejected === attempts.length, unchanged, modal, noOpHistory: HelpEditor.getState().historyIndex === index && !HelpEditor.getState().dirty };
      });
    });

    await run('Reviewed text preserves backward inline selection after a structural edit', page, async () => {
      await load(xml('<p>First paragraph.</p><p>Unique <em>selected text α</em> remains here.</p><p>Final paragraph.</p>'));
      return page.evaluate(() => {
        const visual = document.getElementById('visual-editor'), text = visual.querySelector('em').firstChild;
        visual.focus(); getSelection().setBaseAndExtent(text, 12, text, 1);
        const selected = getSelection().toString(), expected = HelpEditor.getSource();
        HelpEditor.applyReviewedSource('chapters/editor.htm', expected, expected.replace('<p>First paragraph.</p>', '<p>New introduction.</p><p>First paragraph.</p>'));
        const selection = getSelection();
        return { selected: selection.toString() === selected, backward: selection.anchorOffset > selection.focusOffset, node: selection.anchorNode.parentElement.localName === 'em' };
      });
    });

    await run('Review maps a backward selection after longer wording in the same paragraph', page, async () => {
      await load(xml('<p id="same-block">Short start. <em>Keep this selected tail α.</em></p>'));
      return page.evaluate(() => {
        const text = document.querySelector('#same-block em').firstChild; document.getElementById('visual-editor').focus(); getSelection().setBaseAndExtent(text, 23, text, 0);
        const expected = HelpEditor.getSource(), selected = getSelection().toString();
        HelpEditor.applyReviewedSource('chapters/editor.htm', expected, expected.replace('Short start.', 'A much longer revised beginning.'));
        const selection = getSelection(), retained = selection.toString() === selected, backward = selection.anchorNode === selection.focusNode ? selection.anchorOffset > selection.focusOffset : !!(selection.anchorNode.compareDocumentPosition(selection.focusNode) & Node.DOCUMENT_POSITION_PRECEDING);
        const current = HelpEditor.getSource(); HelpEditor.applyReviewedSource('chapters/editor.htm', current, current.replace('Keep this selected tail α.', 'Removed.'));
        const clamped = document.getElementById('visual-editor').contains(getSelection().anchorNode) && document.getElementById('visual-editor').contains(getSelection().focusNode);
        return { retained, backward, removedTextClamped: clamped };
      });
    });

    await run('Source review acceptance retains its buffer and remains one undoable edit', page, async () => {
      await load();
      return page.evaluate(() => {
        HelpEditor.setTab('source'); const field = document.getElementById('source-editor'), before = HelpEditor.getSource(), start = field.value.indexOf('Final paragraph.');
        field.focus(); field.setSelectionRange(start, start + 16); const selected = field.value.slice(field.selectionStart, field.selectionEnd);
        const accepted = before.replace('Bold and italic text α.', 'Bold and clearer text α.');
        HelpEditor.applyReviewedSource('chapters/editor.htm', before, accepted);
        const buffer = HelpEditor.getState().tab === 'source' && HelpEditor.getSource() === accepted && field.value === accepted, selection = field.value.slice(field.selectionStart, field.selectionEnd) === selected;
        HelpEditor.undo(); const undo = HelpEditor.getSource() === before; HelpEditor.redo(); const redo = HelpEditor.getSource() === accepted;
        HelpEditor.markSaved(accepted); const clean = !HelpEditor.getState().dirty;
        field.value = field.value.replace('Final paragraph.', 'Final saved edit.'); field.dispatchEvent(new Event('input', { bubbles: true })); const second = HelpEditor.getSource(); HelpEditor.markSaved(second); HelpEditor.undo();
        return { buffer, selection, undo, redo, clean, secondUndo: HelpEditor.getSource() === accepted && HelpEditor.getState().dirty };
      });
    });

    for (const [format, namespace, prefix] of [['canonical', SDH, 'sdh'], ['Flare import', MC, 'MadCap']]) {
      await run(format + ' paragraph-to-list formatting preserves metadata and source history', page, async () => {
        await load(xml(body(prefix), namespace, prefix));
        await page.evaluate(() => {
          document.getElementById('visual-editor').focus(); const range = document.createRange(); range.setStartBefore(document.getElementById('list-one')); range.setEndAfter(document.getElementById('list-two'));
          getSelection().removeAllRanges(); getSelection().addRange(range); document.dispatchEvent(new Event('selectionchange'));
        });
        await page.click('[data-command="insertUnorderedList"]');
        const bullet = await page.evaluate(({ SDH, code }) => {
          const saved = HelpEditor.getSource(), doc = new DOMParser().parseFromString(saved, 'application/xml'), first = doc.getElementById('list-one');
          return { valid: !doc.querySelector('parsererror'), list: doc.querySelectorAll('ul li').length === 2, condition: first?.getAttributeNS(SDH, 'conditions') === 'Primary.SWHELP', foreign: first?.getAttributeNS('urn:overnight:qa', 'flag') === 'keep', code: doc.querySelector('pre').textContent === code, equation: doc.getElementsByTagNameNS(SDH, 'equation')[0]?.textContent === '$x+1$' };
        }, { SDH, code });
        const bulletSource = await page.evaluate(() => HelpEditor.getSource());
        await page.evaluate(() => { HelpEditor.undo(); HelpEditor.redo(); HelpEditor.setTab('source'); HelpEditor.setTab('visual'); HelpEditor.markSaved(); });
        const history = await page.evaluate(source => HelpEditor.getSource() === source && !HelpEditor.getState().dirty, bulletSource);
        return { ...bullet, history };
      });
    }

    await run('Inserted local images render from the topic base and save authored query spelling', page, async () => {
      await load();
      await page.evaluate(() => {
        const visual = document.getElementById('visual-editor'), paragraph = document.getElementById('tail'); visual.focus(); const range = document.createRange(); range.selectNodeContents(paragraph); range.collapse(false); getSelection().removeAllRanges(); getSelection().addRange(range); document.dispatchEvent(new Event('selectionchange'));
      });
      const authored = '../resources/figures/Caf%C3%A9%20figure.svg?Mode=Upper&mode=A%20B#piece';
      page.once('dialog', dialog => dialog.accept(authored)); await page.click('#insert-image');
      await page.waitForFunction(() => document.querySelector('#visual-editor img')?.complete);
      return page.evaluate(({ authored, base, original }) => {
        const image = document.querySelector('#visual-editor img'), saved = HelpEditor.getSource(), doc = new DOMParser().parseFromString(saved, 'application/xml'), src = doc.querySelector('img').getAttribute('src');
        const rendered = new URL(image.src), expected = new URL(authored, base); rendered.searchParams.delete('__statsdirect_editor_asset');
        const loaded = image.naturalWidth > 0; HelpEditor.undo(); const undo = HelpEditor.getSource() === original; HelpEditor.redo(); const redo = HelpEditor.getSource() === saved; HelpEditor.markSaved(); HelpEditor.setTab('source'); HelpEditor.setTab('visual');
        return { rendered: loaded, topicBase: rendered.origin === expected.origin && rendered.pathname === expected.pathname && rendered.hash === expected.hash && JSON.stringify([...rendered.searchParams]) === JSON.stringify([...expected.searchParams]), source: src === authored, noCacheMetadata: !saved.includes('__statsdirect_editor_asset'), valid: !doc.querySelector('parsererror'), undo, redo, saveAndViews: HelpEditor.getSource() === saved && !HelpEditor.getState().dirty };
      }, { authored, base: payload.baseUrl, original: payload.source });
    });

    await run('Cancelled blank and unsupported image prompts do not change the draft', page, async () => {
      await load();
      for (const value of [null, '', '   ', 'javascript:window.invalidImageExecuted=true']) {
        page.once('dialog', dialog => value === null ? dialog.dismiss() : dialog.accept(value)); await page.click('#insert-image');
        const unchanged = await page.evaluate(original => HelpEditor.getSource() === original && !HelpEditor.getState().dirty && !window.invalidImageExecuted, payload.source);
        if (!unchanged) return { unchanged: false };
      }
      return { unchanged: true };
    });

    await run('Canonical equation LaTeX controls and pointer keyboard preserve portable source', page, async () => {
      await load(); await page.locator('.equation-widget').dblclick();
      await page.locator('#equation-field').locator('[part="virtual-keyboard-toggle"]').click();
      await page.waitForFunction(() => mathVirtualKeyboard.visible);
      const key = page.locator('.ML__keyboard .MLK__keycap[aria-label="7"]').filter({ visible: true }).first();
      const before = await page.locator('#equation-field').evaluate(field => field.value); await key.click();
      const pointer = await page.locator('#equation-field').evaluate((field, before) => field.value !== before, before);
      await page.locator('#latex-options summary').click(); await page.fill('#equation-latex', '\\frac{7}{3}+\\alpha'); await page.uncheck('#equation-display'); await page.click('#equation-apply');
      const result = await page.evaluate(({ SDH, pointer }) => {
        const source = HelpEditor.getSource(), doc = new DOMParser().parseFromString(source, 'application/xml'), equation = doc.getElementsByTagNameNS(SDH, 'equation')[0];
        const portable = /\\frac(?:\{7\}|7)(?:\{3\}|3)/.test(equation.textContent) && equation.textContent.includes('\\alpha') && !equation.textContent.includes('\\displaystyle') && !source.includes('\\placeholder');
        HelpEditor.undo(); const undo = !HelpEditor.getSource().includes('\\frac{7}{3}'); HelpEditor.redo();
        return { pointer, portable, undo, redo: HelpEditor.getSource() === source, dialogClosed: !document.getElementById('equation-dialog').open && !mathVirtualKeyboard.visible };
      }, { SDH, pointer });
      await page.evaluate(() => HelpEditor.setTab('preview'));
      const frame = await (await page.$('#preview-frame')).contentFrame(); await frame.waitForFunction(() => window.helpPreviewReady === true);
      const mathematicalPreview = await frame.evaluate(() => {
        const fraction = document.querySelector('mjx-assistive-mml mfrac');
        return fraction?.children[0]?.textContent === '7' && fraction?.children[1]?.textContent === '3' && [...document.querySelectorAll('mjx-assistive-mml mi')].some(element => element.textContent === 'α') && !document.querySelector('[data-mml-node="merror"],mjx-merror');
      });
      return { ...result, mathematicalPreview };
    });

    await run('Cross-window compound clipboard preserves tables math namespaces and relative artwork', page, async () => {
      const other = await open();
      try {
        const source = xml('<div id="compound"><p>Inline <sdh:equation>$a+b$</sdh:equation> notation.</p><table qa:flag="table"><tbody><tr><td sdh:conditions="Primary.WEBHELP"><a href="../notes/Caf%C3%A9.htm?Mode=Upper#Case-α"><b>Target</b></a><img src="../resources/figures/Caf%C3%A9%20figure.svg?view=A%20B#piece"/></td></tr></tbody></table><qa:note qa:flag="preserve">Unknown α</qa:note></div>');
        await other.evaluate(payload => HelpEditor.load(payload), { ...payload, path: 'first/source.htm', baseUrl: origin + '/project/first/source.htm', source });
        const html = await other.locator('#compound').evaluate(element => element.outerHTML); await load();
        return await page.evaluate(({ html, SDH, origin }) => {
          const visual = document.getElementById('visual-editor'); visual.insertAdjacentHTML('beforeend', html); visual.dispatchEvent(new Event('input', { bubbles: true })); const source = HelpEditor.getSource(), doc = new DOMParser().parseFromString(source, 'application/xml'), compound = doc.getElementById('compound'), image = compound.querySelector('img'), link = compound.querySelector('a');
          return { valid: !doc.querySelector('parsererror'), table: compound.querySelector('table').getAttributeNS('urn:overnight:qa', 'flag') === 'table', condition: compound.querySelector('td').getAttributeNS(SDH, 'conditions') === 'Primary.WEBHELP', unknown: compound.getElementsByTagNameNS('urn:overnight:qa', 'note')[0]?.getAttributeNS('urn:overnight:qa', 'flag') === 'preserve', equation: compound.getElementsByTagNameNS(SDH, 'equation')[0]?.textContent === '$a+b$', image: new URL(image.getAttribute('src'), origin + '/project/chapters/editor.htm').href === origin + '/project/resources/figures/Caf%C3%A9%20figure.svg?view=A%20B#piece', link: decodeURIComponent(new URL(link.getAttribute('href'), origin + '/project/chapters/editor.htm').href) === origin + '/project/notes/Café.htm?Mode=Upper#Case-α', cleanSource: !source.includes('data-source-node') && !source.includes('data-protected-id') && !source.includes('__statsdirect_editor_asset') };
        }, { html, SDH, origin });
      } finally { await other.close(); }
    });

    await run('Undo recovers a damaged specialized paste without discarding the preceding good draft', page, async () => {
      await load();
      return page.evaluate(() => {
        const original = HelpEditor.getSource(), good = original.replace('Final paragraph.', 'Earlier valid unsaved edit.'); HelpEditor.applyReviewedSource('chapters/editor.htm', original, good);
        const visual = document.getElementById('visual-editor'); visual.insertAdjacentHTML('beforeend', '<span class="equation-widget" contenteditable="false" data-protected-id="missing-clipboard-reference">Damaged equation copy</span>'); visual.dispatchEvent(new Event('input', { bubbles: true }));
        let rejected = false; try { HelpEditor.getSource(); } catch { rejected = true; } HelpEditor.undo();
        const recovered = HelpEditor.getSource() === good, priorDraftDirty = HelpEditor.getState().dirty;
        document.getElementById('tail').append(document.createTextNode(' Continue editing.')); visual.dispatchEvent(new Event('input', { bubbles: true }));
        return { rejected, recovered, priorDraftDirty, continued: HelpEditor.getSource().includes('Earlier valid unsaved edit. Continue editing.') };
      });
    });

    await run('Toolbar Undo can remove a damaged first paste from a clean page', page, async () => {
      await load();
      await page.evaluate(() => {
        const visual = document.getElementById('visual-editor'); visual.insertAdjacentHTML('beforeend', '<span class="equation-widget" contenteditable="false" data-protected-id="missing-first-paste">Damaged first paste</span>'); visual.dispatchEvent(new Event('input', { bubbles: true }));
      });
      await page.waitForFunction(() => !document.getElementById('undo').disabled); await page.click('#undo');
      return page.evaluate(original => ({ recovered: HelpEditor.getSource() === original, clean: !HelpEditor.getState().dirty, disabledAgain: document.getElementById('undo').disabled }), payload.source);
    });

    await run('Host search and save shortcuts expose the current draft without applying a dialog', page, async () => {
      await load();
      await select(page, '#prose'); await page.keyboard.press('End'); await page.keyboard.type(' Current draft.');
      await page.keyboard.press('Control+Shift+h'); await page.keyboard.press('Control+s');
      return page.evaluate(() => {
        const source = HelpEditor.getSource(), saved = hostMessages.findLast(message => message.action === 'save');
        return { searchBridge: hostMessages.some(message => message.action === 'search-replace'), saveBridge: saved?.path === 'chapters/editor.htm' && saved?.source === source && source.includes('Current draft.'), stillDirty: HelpEditor.getState().dirty };
      });
    });

    await run('Mailto and invalid link drafts preserve formatting and route through the host', page, async () => {
      await load(); await select(page, '#prose'); await page.click('#insert-link'); await page.check('input[name="link-mode"][value="external"]');
      await page.fill('#link-url', 'javascript:window.invalidLinkExecuted=true'); await page.click('#link-apply');
      const rejected = await page.evaluate(expected => document.getElementById('link-dialog').open && !document.getElementById('link-error').hidden && HelpEditor.getSource() === expected && !window.invalidLinkExecuted, payload.source);
      await page.fill('#link-url', 'mailto:editor+stats@example.invalid?subject=Caf%C3%A9%20%CE%B1&body=One%20%26%20two');
      await page.click('#link-open'); const beforeApply = await page.evaluate(() => HelpEditor.getSource()); await page.click('#link-apply');
      return page.evaluate(({ rejected, beforeApply, original }) => {
        const source = HelpEditor.getSource(), doc = new DOMParser().parseFromString(source, 'application/xml'), link = doc.querySelector('#prose a'), message = hostMessages.findLast(message => message.action === 'open-link');
        return { rejected, draftOpenDidNotApply: beforeApply === original, email: link?.getAttribute('href') === 'mailto:editor+stats@example.invalid?subject=Caf%C3%A9%20%CE%B1&body=One%20%26%20two', text: link?.textContent === 'Bold and italic text α.', hostOnly: message?.newWindow === true && message.href.startsWith('mailto:editor+stats@example.invalid?'), valid: !doc.querySelector('parsererror') };
      }, { rejected, beforeApply, original: payload.source });
    });

    await run('Review acceptance in Browser view requests regeneration and survives all authoring views', page, async () => {
      await load();
      await page.evaluate(() => { HelpEditor.setUnifiedViews(true); HelpEditor.setOutputView('browser'); HelpEditor.setGeneratedPreview(location.origin + '/generated/review.htm#start'); });
      const frame = await (await page.$('#preview-frame')).contentFrame(); await frame.waitForSelector('#reader');
      return page.evaluate(() => {
        hostMessages.length = 0; const before = HelpEditor.getSource(), accepted = before.replace('Final paragraph.', 'Reviewed final paragraph.');
        HelpEditor.applyReviewedSource('chapters/editor.htm', before, accepted);
        const regeneration = hostMessages.some(message => message.action === 'preview-request' && message.view === 'browser');
        HelpEditor.setOutputView('app'); const app = HelpEditor.getSource() === accepted && HelpEditor.getState().pageView === 'app';
        HelpEditor.setOutputView('source'); const source = document.getElementById('source-editor').value === accepted;
        HelpEditor.setOutputView('formatted'); HelpEditor.undo(); const undo = HelpEditor.getSource() === before; HelpEditor.redo();
        return { regeneration, app, source, undo, redo: HelpEditor.getSource() === accepted && HelpEditor.getState().dirty };
      });
    });
    checks.push({ name: 'No unhandled errors or external network requests', passed: errors.length === 0 && external.length === 0, detail: { errors, external } });
    await context.close();
  } catch (error) { checks.push({ name: 'Browser workflow harness completed', passed: false, detail: error.stack || String(error) }); }
  finally {
    if (browser) await browser.close(); server.close();
    const report = { checkedAtUtc: new Date().toISOString(), passed: checks.length > 0 && checks.every(check => check.passed), fixtures: 'In-memory isolated topics only', limitations: ['Native Save/search dialogs are stubbed at the WebView message bridge.', 'AI review acceptance uses supplied fixture text; no OpenAI request is made.', 'Rich clipboard insertion is simulated; the system clipboard is not accessed.'], checks, errors, external };
    fs.writeFileSync(path.join(evidence, 'workflows.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2)); if (!report.passed) process.exitCode = 1;
  }
})();
