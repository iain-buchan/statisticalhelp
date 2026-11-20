import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {marked} from 'marked';
import './rich-text.mjs';
import {readBlocks, findBlock} from './code-blocks.mjs';

// One set of example replies for the page, the reference reader and each host's own reader (HOST-CONTRACT.md, Code blocks).
const examples = JSON.parse(fs.readFileSync(new URL('./code-block-examples.json', import.meta.url), 'utf8'));
const unescape = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;|&#x27;/g, "'").replace(/&amp;/g, '&');
// The blocks as the page renders them: the label says whether a block is R, and the code element holds the text a click sends.
const pageBlocks = markdown => [...marked(markdown).matchAll(/<div class="code-block"><div class="code-actions"><span>(R|Code)<\/span>[\s\S]*?<pre><code>([\s\S]*?)<\/code><\/pre><\/div>/g)]
  .map(([, label, code]) => ({r: label === 'R', shown: unescape(code.replace(/<[^>]+>/g, ''))}));

test('the examples are well formed', () => {
  const names = examples.cases.map(c => c.name);
  assert.equal(new Set(names).size, names.length, 'case names are unique');
  for (const c of examples.cases) {
    assert.equal(typeof c.markdown, 'string', c.name);
    for (const b of [...c.blocks, ...(c.page ?? [])]) assert.ok(typeof b.r === 'boolean' && typeof b.shown === 'string', c.name);
  }
  for (const m of examples.matches) assert.ok(names.includes(m.case), m.name);
});
test('the page shows the code blocks the examples give', () => {
  for (const c of examples.cases) assert.deepEqual(pageBlocks(c.markdown), c.page ?? c.blocks.map(({r, shown}) => ({r, shown})), c.name);
});
test('the reference reader returns the code blocks the examples give', () => {
  for (const c of examples.cases) assert.deepEqual(readBlocks(c.markdown), c.blocks.map(b => ({r: b.r, shown: b.shown, text: b.text ?? b.shown})), c.name);
});
test('the page differs from the readers only in the cases recorded as differences', () => {
  // A new difference is a decision, not an accident: a case gains "page" here and in this list together.
  assert.deepEqual(examples.cases.filter(c => c.page).map(c => c.name), [
    'a short line in a block under a numbered step, after a blank line',
    'an unclosed block at the end of a reply',
    'a fence indented four spaces outside a list',
    'a quoted block with no closing fence',
    'an indented tilde fence outside a list',
    'a fence indented beyond its numbered step, with a shorter code line',
  ]);
});
test('a click is matched to a block as the examples give', () => {
  for (const m of examples.matches) {
    const blocks = readBlocks(examples.cases.find(c => c.name === m.case).markdown);
    assert.equal(findBlock(blocks, m.position, m.shown, m.rOnly), m.expect, m.name);
  }
});
test('a host opens or copies the reply\'s own characters, with leading tabs kept', () => {
  const [block] = readBlocks('```r\nf <- function(x) {\n\tx + 1\n}\n```');
  assert.equal(block.shown, 'f <- function(x) {\n    x + 1\n}');
  assert.equal(block.text, 'f <- function(x) {\n\tx + 1\n}');
});
