import test from 'node:test';
import assert from 'node:assert/strict';
import {marked} from 'marked';
import * as richText from './rich-text.mjs';
// rich-text.mjs needs a document to sanitise; here the test proves its imports resolve by name and that marked renders.
test('rich-text.mjs loads with its dependencies resolved by name, and marked renders Markdown',()=>{
  assert.equal(typeof richText.richText,'function');assert.equal(typeof richText.bindRichText,'function');
  const html=marked('# Title\n\nSome *text* and `code`.');
  assert.match(html,/<h1[^>]*>Title<\/h1>/);assert.match(html,/<em>text<\/em>/);assert.match(html,/<code>code<\/code>/);
});
test('R fence language tokens accept options separated by ASCII spaces or tabs', () => {
  for (const info of ['r echo=FALSE', 'r\techo=FALSE', 'R', 'Rscript', 'RSCRIPT\techo=FALSE']) {
    const html = marked(`\`\`\`${info}\nmean(c(1, 2))\n\`\`\``);
    assert.match(html, /<span>R<\/span>/, info);
    assert.match(html, /data-code="r">Open in R<\/button>/, info);
    assert.match(html, /class="hljs-/, info);
  }
  const tildeFence = marked('~~~r title=`Example`\nmean(c(1, 2))\n~~~');
  assert.match(tildeFence, /data-code="r">Open in R<\/button>/);
});
test('brace chunks and non-R fences retain ordinary code actions', () => {
  for (const info of ['{r}', '{r echo=FALSE}', 'python', '']) {
    const html = marked(`\`\`\`${info}\nvalue < 3\n\`\`\``);
    assert.match(html, /<span>Code<\/span>/, info);
    assert.match(html, /data-code="copy">Copy<\/button>/, info);
    assert.doesNotMatch(html, /data-code="r"/, info);
    assert.match(html, /value &lt; 3/, info);
  }
});
test('an incomplete R fence keeps its code and actions while a reply is streaming', () => {
  const html = marked('\`\`\`r echo=FALSE\nmean(c(1, 2))');
  assert.match(html, /data-code="r">Open in R<\/button>/);
  assert.match(html, /mean/);
});
test('a mixed three-block message keeps R actions in R-block order', () => {
  const markdown = [
    '\`\`\`r echo=FALSE\nfirst <- 1\n\`\`\`',
    '\`\`\`python\nmiddle = 2\n\`\`\`',
    '\`\`\`RSCRIPT\techo=FALSE\nlast <- 3\n\`\`\`',
  ].join('\n\n');
  const html = marked(markdown);
  const blocks = [...html.matchAll(/<div class="code-block">([\s\S]*?)<\/code><\/pre><\/div>/g)];
  assert.equal(blocks.length, 3);
  assert.deepEqual(blocks.map(([, block]) => /data-code="r"/.test(block)), [true, false, true]);
  assert.deepEqual(blocks.map(([, block]) => /data-code="copy"/.test(block)), [true, true, true]);
  assert.match(blocks[0][1], /first/);
  assert.match(blocks[1][1], /middle/);
  assert.match(blocks[2][1], /last/);
});
