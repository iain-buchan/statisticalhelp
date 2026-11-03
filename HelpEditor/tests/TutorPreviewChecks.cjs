// Browser-only consumer checks for HTML produced by TutorEditorForm.BuildPreviewHtml.
// Native authoring checks supply fixtures; this does not build or edit tutor source.
const fs = require('node:fs'), path = require('node:path'), url = require('node:url'), crypto = require('node:crypto');
const { chromium } = require('./browser-runtime.cjs');
const args = process.argv.slice(2), option = key => { const i = args.indexOf(key); return i < 0 ? undefined : args[i + 1]; };
const root = path.resolve(__dirname, '..');
const fixtures = path.resolve(option('--fixtures') || path.join(root, 'evidence/TutorPreviewFixtures'));
const evidence = path.resolve(option('--evidence') || path.join(root, 'evidence/TutorPreview-' + crypto.randomUUID().replaceAll('-', '')));
fs.mkdirSync(evidence, { recursive: true });
const checks = [], errors = [], requests = [], dialogs = [];
let browser;
const record = (name, passed, detail) => checks.push({ name, passed: !!passed, ...(detail === undefined ? {} : { detail }) });
const digest = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const normal = value => String(value).replaceAll('\r\n', '\n').replaceAll('\u00a0', ' ').trim();

(async () => {
  let sourceHashes;
  try {
    const expected = JSON.parse(fs.readFileSync(path.join(fixtures, 'expectation.json'), 'utf8'));
    for (const key of ['correctID', 'wrongID', 'correctFeedback', 'wrongFeedback', 'explanation', 'stem', 'r'])
      if (typeof expected[key] !== 'string') throw Error('Fixture expectation must contain string ' + key);
    if (expected.correctID === expected.wrongID) throw Error('Fixture requires different correct/wrong choice IDs.');
    const question = path.join(fixtures, 'question.html'), lesson = path.join(fixtures, 'lesson.html');
    sourceHashes = { question: digest(question), lesson: digest(lesson), expectations: digest(path.join(fixtures, 'expectation.json')) };
    browser = await chromium.launch({ channel: process.env.STATSDIRECT_BROWSER_CHANNEL || 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1100, height: 850 } });
    await context.route('**/*', async route => {
      if (/^(file:|data:|about:)/.test(route.request().url())) return route.continue();
      requests.push(route.request().url()); await route.abort();
    });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    page.on('dialog', async dialog => { dialogs.push(dialog.message()); await dialog.dismiss(); });
    await page.goto(url.pathToFileURL(question).href);
    record('Actual question HTML loads locally with feedback hidden', page.url().startsWith('file:') && await page.locator('#feedback').isHidden());
    record('Authored question text survives HTML-sensitive characters literally', normal(await page.locator('main > p').first().innerText()) === normal(expected.stem));
    record('Authored script-like markup creates no executable element or dialog', await page.locator('main > p script,main > p img,main > p iframe').count() === 0 && dialogs.length === 0 && await page.evaluate(() => !window.__tutorCodeRan && !window.__tutorUnexpectedExecuted));
    await page.getByRole('button', { name: 'Check answer', exact: true }).click();
    record('Checking without selecting an option does not reveal the key', await page.locator('#feedback').isHidden() && await page.locator('input:checked').count() === 0);
    await page.locator('input[name="answer"]').evaluateAll((inputs, value) => { const selected = inputs.find(input => input.value === value); if (!selected) throw Error('Missing wrong option'); selected.click(); }, expected.wrongID);
    await page.getByRole('button', { name: 'Check answer', exact: true }).click();
    const wrong = await page.locator('#feedback').innerText();
    record('Wrong answer shows its own feedback and the full explanation', await page.locator('#feedback').isVisible() && wrong.startsWith('Review this choice. ') && wrong.includes(expected.wrongFeedback) && wrong.includes(expected.explanation));
    await page.locator('input[name="answer"]').evaluateAll((inputs, value) => { const selected = inputs.find(input => input.value === value); if (!selected) throw Error('Missing correct option'); selected.click(); }, expected.correctID);
    await page.getByRole('button', { name: 'Check answer', exact: true }).click();
    const correct = await page.locator('#feedback').innerText();
    record('Correct answer follows the authored key and its own feedback', correct.startsWith('Correct. ') && correct.includes(expected.correctFeedback) && correct.includes(expected.explanation));
    record('Answer feedback is rendered as text rather than active authored HTML', await page.locator('#feedback script,#feedback img,#feedback iframe').count() === 0 && dialogs.length === 0);
    await page.screenshot({ path: path.join(evidence, 'question-feedback.png'), fullPage: true });
    await page.reload();
    record('Reload starts a fresh preview without a saved learner attempt', await page.locator('#feedback').isHidden() && await page.locator('input:checked').count() === 0 && await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0));
    await page.setViewportSize({ width: 650, height: 850 });
    record('Question preview remains within a narrow authoring pane', await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    await page.goto(url.pathToFileURL(lesson).href);
    record('Actual lesson HTML displays the R example without a Run control', await page.locator('pre').count() >= 1 && await page.getByRole('button', { name: /^run/i }).count() === 0);
    record('Displayed R code retains its complete text and line breaks', normal(await page.locator('pre').first().innerText()) === normal(expected.r));
    record('R example is inert text with no active HTML embedded in it', await page.locator('pre script,pre img,pre iframe').count() === 0 && await page.evaluate(() => !window.__tutorCodeRan && !window.__tutorUnexpectedExecuted));
    record('Lesson preview remains within a narrow authoring pane', await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    await page.screenshot({ path: path.join(evidence, 'lesson-r-example.png'), fullPage: true });
    record('Both previews work without scripts failing, dialogs or external requests', errors.length === 0 && requests.length === 0 && dialogs.length === 0, { errors, requests, dialogs });
    record('Browser checks leave the generated fixtures unchanged', digest(question) === sourceHashes.question && digest(lesson) === sourceHashes.lesson && digest(path.join(fixtures, 'expectation.json')) === sourceHashes.expectations);
    await context.close();
  } catch (error) { record('Browser preview checks complete', false, error.stack || String(error)); }
  finally {
    if (browser) await browser.close();
    const report = { passed: checks.length > 0 && checks.every(check => check.passed), checkedAtUtc: new Date().toISOString(), fixtures, sourceHashes, noBuildOrTutorSourceWrites: true, checks, errors, requests, dialogs };
    fs.writeFileSync(path.join(evidence, 'checks.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ passed: report.passed, checks: checks.length, evidence, failures: checks.filter(check => !check.passed) }, null, 2));
    if (!report.passed) process.exitCode = 1;
  }
})();
