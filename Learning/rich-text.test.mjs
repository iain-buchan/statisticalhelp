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
