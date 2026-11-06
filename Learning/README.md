# StatsDirect Learning: the shared code

This folder is the single source of the Learning page that both StatsDirect applications
bundle: the biostatistics tutor, lessons, practice questions, learning pathways, the local
learning record and its exports. It is platform-neutral. Each application consumes it as a
Git submodule pinned to a commit of this repository and bundles it with its own tooling.

Lessons, questions and pathways are content, not code. They are authored in the StatsDirect
Help Editor and published as `Published/Tutor` in this repository; each application imports
that publication separately from the code here.

## What is here

- `app.mjs` and the modules beside it: the page and its logic. `host.mjs` is the bridge to the
  host application.
- `index.html` and `learn.css`: the page and its stylesheet.
- `host-contract.json` and `HOST-CONTRACT.md`: the contract between this code and a host, as a
  version number and the document that defines it (actions the page posts, callbacks it
  defines, payloads, the `lessons.json` fields it reads, and the bump rule).
- `package.json` and `pnpm-lock.yaml`: the dependencies, one pinned set for every host.
- `*.test.mjs`, `test-config.mjs` and `lesson-checks.mjs`: the unit tests and their
  configuration.

## How a host consumes it

1. Add this repository as a submodule and pin it to a commit; move the pin only by a deliberate
   commit in the application repository, and record the commit and the file hashes in the
   application's own manifest. Tags `learning-vMAJOR.MINOR.PATCH` mark the commits applications
   have pinned.
2. Bundle `app.mjs` with the host's bundler. Two imports are remapped by every host:
   `../Content/Learn/lessons.json` to the imported published lessons, and `./bank.mjs` to the
   host's provenance shim (`bankPublication` may be absent; the page treats it as null).
3. Register `window.statsDirectHost` before the page runs, with `platform` and `postMessage`,
   as `HOST-CONTRACT.md` describes, and compile in the contract number: compare
   `host-contract.json` with the host's constant at build time and in the `ready` handler at
   load, and refuse a mismatch.

## Tests

The tests run from a host's own root, not from this folder: the host supplies its `bank.mjs`
beside the package (its remap or a copy), its `node_modules` above the package, and three
variables: `STATSDIRECT_CONTENT_ROOT` (required; the folder holding `Learn/lessons.json`,
`Learn/provider-course-example.json`, `analysis-menu.json` and `Help/`), `STATSDIRECT_RSCRIPT`
(the Rscript to run; `RSCRIPT` is also read), and `STATSDIRECT_LEARN_ARCHIVE` (optional; an
archived question bank to compare against; unset skips that comparison, named but missing fails).
The Windows host runs them through `tests/Learning/run-shared.mjs`, the Mac through
`Learn/test-register.mjs`; each runs them from its submodule path in its own continuous integration.

## Conventions

- Line endings are LF in every file here, whatever the platform; `.gitattributes` enforces it.
- Either development session commits to this folder on `master`; an application takes a change
  only by moving its pin.
- Behaviour that differs between hosts is selected by the platform value the host supplies,
  never by separate copies of a file.

## Provenance

Created on 6 November 2026 from the platform-neutral Learning sources of
`iain-buchan/statsdirect-mac` at commit 4597c165044ec64d39a5b29de4d05f7568c1eacb, by decision of
Iain Buchan, replacing the copy that the Windows application carried under `Learning/upstream`.
