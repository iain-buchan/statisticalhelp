// Where the shared Learning tests find their inputs. The defaults are the Mac repository's layout;
// a host points the tests elsewhere with STATSDIRECT_CONTENT_ROOT (the folder holding Learn/lessons.json,
// Learn/provider-course-example.json, analysis-menu.json and Help/), STATSDIRECT_LEARN_ARCHIVE (an archived
// question bank; required when named) and STATSDIRECT_RSCRIPT (or the repository's RSCRIPT).
import {execFileSync} from 'node:child_process';
import {mkdtempSync, writeFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';
const folder = value => pathToFileURL(value.endsWith('/') || value.endsWith('\\') ? value : value + '/');
export const contentRoot = process.env.STATSDIRECT_CONTENT_ROOT ? folder(process.env.STATSDIRECT_CONTENT_ROOT) : new URL('../Content/', import.meta.url);
export const contentFile = relative => new URL(relative, contentRoot);
export const archiveRequested = Boolean(process.env.STATSDIRECT_LEARN_ARCHIVE);
export const archivedBank = archiveRequested ? pathToFileURL(process.env.STATSDIRECT_LEARN_ARCHIVE) : new URL('../Docs/Learn/bank.mjs', import.meta.url);
export const rscript = process.env.STATSDIRECT_RSCRIPT || process.env.RSCRIPT || (process.platform === 'darwin' ? '/Library/Frameworks/R.framework/Resources/bin/Rscript' : 'Rscript');
// Runs Rscript on a script file, or on the expressions given after -e, which are written in order to one temporary
// file first because Rscript.exe does not keep a multiline -e argument intact. Output is returned as text. On Windows
// the C locale keeps R's output ASCII, as the Windows test runner has always done; other hosts keep their locale.
export function runR(args, options = {}) {
  let temp = null;
  try {
    if (args.includes('-e')) {
      const expressions = [], rest = [];
      for (let i = 0; i < args.length; i++) { if (args[i] === '-e') expressions.push(args[++i]); else rest.push(args[i]); }
      temp = mkdtempSync(join(tmpdir(), 'statsdirect-r-'));
      const file = join(temp, 'expression.R');
      writeFileSync(file, expressions.join('\n'));
      args = [...rest, file];
    }
    const env = {...process.env, ...(process.platform === 'win32' ? {LC_ALL: 'C', LANG: 'C'} : {}), ...(options.env ?? {})};
    return execFileSync(rscript, args, {encoding: 'utf8', ...options, env});
  } finally { if (temp) rmSync(temp, {recursive: true, force: true}); }
}
