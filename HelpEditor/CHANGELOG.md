# Changelog

## Unreleased

- Standalone website exports preserve publication ZIPs and ask for a different filename.
- Generated help, output receipts, the desktop delivery receipt, the shared-content manifest and the built-in tutor payload are written with LF on every platform, so a publication's bytes depend on its source alone rather than on the publishing machine.
- Double-clicking a Contents node expands or collapses it once; the extra toggle that reversed the native one a moment later is gone.
- Native Contents checks support optional method arguments and close their owned window even when cleanup fails. A forced-failure check covers that exit path; CI bounds the native step to 15 minutes.
- Imported pages and rich-text paste remove executable attributes and embedded content from the draft. Save and project-wide replacement reject prohibited source before writing; equations, R examples and ordinary links are preserved.
- Authoring and preview WebViews share settings, with cross-origin script access to local project files refused while topic images remain available.
- Tutor course export remembers its last successful folder and otherwise starts in Documents. The website ZIP destination remains a separate preference.
- Publish content captures saved help and tutor source together, exports the website ZIP, and commits the website, desktop help and tutor build inputs to `statisticalhelp` with a complete hashed manifest. Windows build inputs receive the identical desktop and tutor files.
- Shared content is validated before publication; changed tutor material still requires its recorded author review. Application builds can pin one publication revision without updating installed applications at runtime.
- Contents navigation in Browser view keeps the editor window and current preview stable instead of flashing between views.

## 1.0.4 — 6 November 2026

- Desktop Help and the editor's App preview use the native **Home**, **Browse topics** and search controls. Parent titles expand the topic tree in place, with a separate **Overview** link for each parent page. Desktop Help opened in a narrow browser window keeps a compact **Browse topics** button.

## 1.0.0 — 4 November 2026

- One Publish help operation captures saved source, builds Public/Desktop once, saves the website ZIP, commits and pushes matching source plus the GitHub website, and delivers verified desktop help to StatsDirect's build inputs. Destinations are remembered; failures retain per-target evidence and never claim all outputs completed.

- Explicit, verified desktop-help delivery from saved source, with reviewed Flare adoption, file-hash receipts and interrupted-publication recovery.
- Read-only source comparison with namespace-equivalent comparisons and separate text, links, equations, code, markup and Contents differences.
- Contents editing preserves comments, unknown XML, attribute order and exact bytes for an unchanged tree; stale trees cannot overwrite newer edits.
- Opening a local link resolves that page directly, without parsing every other topic.
- Machine-specific source protection is private configuration. Path checks recognise benign Windows cloud placeholders while refusing links and unsafe reparse points.
- Git runs the executable resolved from PATH rather than searching the current folder.
- Frozen Flare outputs and pinned browser dependencies make the website, equation and appearance regression checks reproducible without Flare.
- CI includes browser and Flare comparisons as well as native/core checks; action versions are pinned to reviewed commits.

This release leaves the main StatsDirect application and its installers unchanged.

## 0.1.3

- Tutor authoring and multiple-choice-question research, learner goals, difficulty levels and reviewed tutor publishing.
- Website ZIP export with remembered destination; installed workspace and data-directory fixes.

## 0.1.2

- Independent StatsDirect authoring schema, preserving legacy Flare import and equation artwork.

## 0.1.1

- Help Editor icon and desktop shortcut.

## 0.1.0

- Initial independent page, equation and Contents editor with website/desktop generation, search, preview, recovery, GitHub publication and ChatGPT review.
