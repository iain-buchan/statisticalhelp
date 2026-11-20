// The reference reader for fenced code in a reply, and the rule that matches a clicked block to it.
//
// The conversation page renders a reply with marked (rich-text.mjs). Each host also reads the reply
// itself, so that Open in R and Copy only ever use code the host finds in its own saved copy. This file
// states that reader once, in plain JavaScript. The Mac and Windows hosts mirror it in their own
// languages, and code-block-examples.json holds the replies that the page, this file and both hosts
// are tested against (code-blocks.test.mjs here; each host has its own test). The page does not import it.

// marked expands a line's leading tabs before it reads the line: leading spaces, then a run of tabs, four spaces each.
const expandTabs = line => line.replace(/^( *)(\t+)/, (_, spaces, tabs) => spaces + '    '.repeat(tabs.length));

// Removes up to `limit` block-quote markers (spaces, ">", one optional space or tab) from the start of a line.
function unquote(line, limit = Infinity) {
  let depth = 0, marker;
  while (depth < limit && (marker = /^ *>[ \t]?/.exec(line))) { line = line.slice(marker[0].length); depth++; }
  return {line, depth};
}

// An opening fence: any spaces, then three or more backticks with no backtick in the rest of the line,
// or three or more tildes with anything after. It may also sit on a list marker's own line ("- ```r",
// "1. ```r"); the marker and the spaces after it then count as indent. The language is the first word of the label.
function opener(line) {
  const m = /^( *(?:(?:[-*+]|\d{1,9}[.)]) {1,4})?)(`{3,}|~{3,})(.*)$/.exec(line);
  if (!m || (m[2][0] === '`' && m[3].includes('`'))) return null;
  return {indent: m[1].length, fence: m[2], language: m[3].trim().split(/[ \t]+/, 1)[0].toLowerCase()};
}

// A closing fence, as marked reads it: spaces (at most three more than the opener's indent), the opener's
// own fence, any further backticks or tildes, then only spaces.
function closes(line, open) {
  const spaces = line.search(/[^ ]|$/), body = line.slice(spaces);
  return spaces <= open.indent + 3 && body.startsWith(open.fence) && /^[`~]* *$/.test(body.slice(open.fence.length));
}

/**
 * The completed fenced blocks of a reply, in order. Each is {r, shown, text}:
 *   r      whether its language is r or rscript (any letter case);
 *   shown  the code as the page shows it (leading tabs as spaces), which is what a click sends;
 *   text   the code as the reply has it (leading tabs kept), which is what a host opens or copies.
 * A block with no closing fence is not returned.
 */
export function readBlocks(markdown) {
  const original = String(markdown ?? '').replace(/\r\n|\r/g, '\n').split('\n');
  const lines = original.map(expandTabs);
  const blocks = [];
  for (let i = 0; i < lines.length; i++) {
    const quoted = unquote(lines[i]);
    const open = opener(quoted.line);
    if (!open) continue;
    const shown = [], text = [];
    let closed = false;
    while (++i < lines.length) {
      // A line inside the block loses as many quote markers as its opener had, then the opener's indent,
      // but only when it begins with at least that many spaces; a line indented less is left as it is.
      const rest = unquote(lines[i], quoted.depth).line;
      if (closes(rest, open)) { closed = true; break; }
      const code = rest.startsWith(' '.repeat(open.indent)) ? rest.slice(open.indent) : rest;
      const removed = lines[i].length - code.length;
      shown.push(code);
      // The reply's own characters when the removed prefix is literally there; otherwise the shown line.
      text.push(original[i].startsWith(lines[i].slice(0, removed)) ? original[i].slice(removed) : code);
    }
    if (closed) blocks.push({r: open.language === 'r' || open.language === 'rscript', shown: shown.join('\n'), text: text.join('\n')});
  }
  return blocks;
}

/**
 * Which block a click means. `position` is the block's place among the page's buttons (among R blocks
 * when rOnly, among all blocks otherwise) and `shown` is the code text the page sent with the click.
 * Returns the index in that same list (R blocks when rOnly, else all blocks), or -1.
 * The block at the position wins when its shown text is the page's text; otherwise the first block
 * with that text; otherwise there is no match, and a host says so instead of using another block.
 */
export function findBlock(blocks, position, shown, rOnly = true) {
  const list = rOnly ? blocks.filter(block => block.r) : blocks;
  if (Number.isInteger(position) && position >= 0 && position < list.length && list[position].shown === shown) return position;
  return list.findIndex(block => block.shown === shown);
}
