# 1.0.0 release gate

The first author is Iain. The priority is reliable Website/GitHub and StatsDirect DesktopHelp output. Flare source and published releases are retained while the replacement is verified; passing local checks does not publish a release or delete either source tree.

The gate comprises:

- A clean C# solution build and all affected core/native authoring checks, including Contents preservation, stale-edit conflicts, recovery, links and unsaved drafts.
- Fresh headless DesktopHelp and Website builds of all 404 maintained topics, 403 searchable documents, no unreviewed warnings, all local assets, context mappings, equations, R dropdowns and target conditions.
- Frozen, hashed outputs from all three Flare targets, compared without a Flare installation. Intentional reference/equation/navigation changes are explicit fixtures rather than widened tolerances.
- Browser authoring, offline navigation/search, local links, responsive layout, equation rendering and original-style comparisons.
- Website ZIP extraction and complete delivered-file checks; no profiles, credentials, recovery archives or authoring source in output.
- A full isolated copy of the actual Windows StatsDirect source: desktop delivery, real application build, exact copied file hashes and production help-host navigation, search, contexts and docking.
- Forced interruptions during desktop publication, deterministic recovery and refusal of unknown or concurrently changed output.
- A final source/installers/protected-path comparison to demonstrate that tests did not alter the published application or original Flare source.

Evidence for this candidate is under `evidence/ReviewImprovements-20261104`. These are local verification records, not release assets. The repeatable developer entry points are `Test-HelpEditor.ps1 -CompareFlare`, `tests/Run-BrowserChecks.ps1` and the dedicated console/native check projects in the solution. Browser dependencies are developer-only and pinned under `tests/browser`; CI uses the frozen baseline.

Before publishing 1.0.0: commit the reviewed source, run GitHub CI on that exact commit, then build, sign and verify its installer against the committed inventory. The signed 0.1.3 Help Editor installer and main StatsDirect releases remain unchanged until a release is explicitly requested.

## Unified publication and ongoing content checks

The release includes **File > Publish help…**, which captures one saved-source/runtime snapshot and builds Public and Desktop once. The website ZIP and committed root `Docs` use the same reader bytes; desktop help carries the same snapshot identifier. Default-branch publication is explicitly acknowledged. The private Git checkout pins the selected origin, branch and reviewed commit, preserves authored/code bytes through fresh CRLF checkouts, and leaves the author's original source/checkout unchanged. Per-target receipts distinguish completed local outputs, a successful push and unverified GitHub Pages deployment.

This extension passed a full 404-topic end-to-end publication test, adversarial local-remote tests, legacy Git/website/desktop regressions, and the real publication dialog at 240 DPI. No test pushed a real website or changed the main application. Detailed extension evidence is under `evidence/HelpEditor100Release`.

Historical fidelity tests use current implementation code with the immutable source at commit `96c2bb257064a1113514a781f796ac3ecb8caf8b`; `RegressionSourceFixture.json` verifies all 800 source-file bytes and its inventory digest. They run in an isolated short workspace. A separate maintained-content gate derives topic/search/context counts from actual current source and checks both outputs and local links/assets. Thus valid future additions and wording changes do not fail a historical 404-page comparison. Actual installer packaging always uses the current checkout's maintained source, never the historical fixture.

## Local verification on 4 November 2026

| Area | Result |
| --- | --- |
| C# solution | Release build, zero warnings/errors |
| Non-GUI regression | 19 suites, 2,766 assertions passed; one optional linked-archive case skipped for unavailable Windows symlink privileges |
| Desktop delivery and source comparison | 80 assertions passed, including full corpus and forced process termination/recovery |
| New native delivery and CLI integration | 55 assertions passed, including 37 command invocations and unsaved-draft preservation |
| Existing native authoring | All 16 programs passed; supplementary tutor preview and research-dialog checks passed |
| Both fresh outputs | 404 topics and 403 search entries each, zero build warnings |
| Browser authoring/output/parity | All 30 original selected script runs passed; added navigation/printing checks and final 12-script output run passed |
| Independent Website ZIP reader | All 404 exported pages loaded offline; 16 consumer assertions passed |
| Actual Windows consumer | Isolated full application built; all 875 delivered files match copied output; 404 context names/IDs/destinations verified; 21 native host assertions passed, including all 291 SVGs |
| Installer content | Unsigned 1.0.0 candidate built; administrative extraction/package audit passed, all 1,678 payload files matched; no installation or signing performed |
| Source comparison | Original working Flare source and HelpSource have 403/404 equivalent topics and identical Contents. The one topic difference is the deliberately improved reference list |
| Protected inputs | Maintained source, original main repository and released installers preserved |

The full 404-page zero-tolerance pixel comparison is **not universally pixel-identical**: 347 pages are exact and 57 differ. Independent review classified all 57: 49 minor deterministic raster differences (1–428 changed pixels with unchanged geometry), five propagated Contents changes, two known saved-text changes, and one 1-pixel navigation alignment. There are no page/network failures, unexpected heading/image changes or material topic-layout regressions. The strict pixel report remains failed; no tolerance was relaxed to relabel it as a pass. The source comparison and detailed classification remain evidence rather than a claim that every screenshot is identical.

An initial native consumer test used an excessively long clone path: WebView2 failed to load a 267-character physical SVG path. The identical, hash-verified tree passes from a short location, and the installed/candidate paths on this PC are below that limit. Keep help, temporary extraction and preview roots short; Windows Installer administrative extraction has a similar legacy path limit. The audit harness now chooses and checks a short native temp root. This does not require a change to the main StatsDirect application.

Local verification supports retiring Flare for this single-author workflow. It is not certification of an unpublished signed release: the next steps remain a reviewed source commit, CI on that exact commit and a signed, verified installer. Keep the original source and frozen Flare baseline as provenance; retiring the licence does not require deleting them.
