# Learning host contract

Contract 1, 6 November 2026. The Learning page (`app.mjs` and the modules beside it, bundled by each host) and the host application agree on what follows. `host-contract.json` carries the number. The page sends it in `ready` and exposes it as `window.statsDirectLearn.hostContract`.

Each host is to compile in the number it implements and refuse a mismatch twice: at build, where its importer or bundler compares `host-contract.json` with the host's constant, and at load, where its `ready` handler answers `loadError` and stops. On 6 November 2026 the page sends and exposes the number and neither host yet reads it; the Mac host and the Windows host add the two checks when they take the package as a submodule, and until then a mismatch is caught only by the tests and suites of each build.

Bump the number on any addition, removal or renaming of an action, a callback, a required field of either, a `lessons.json` field a host reads, a kind string, or an element id a host touches. Optional additive fields do not bump it.

## The bridge

The host registers `window.statsDirectHost` before the page runs: an object with `platform` (`mac` or `windows`) and `postMessage(body)`. Until every host registers it, the page falls back to the WebKit handler `window.webkit.messageHandlers.statsDirectLearn`, which the Windows host shims on WebView2 (`host.mjs`). The Windows host wraps every message it receives in its own `viewGeneration` envelope and drops messages from an earlier generation; that envelope is the host's, outside this contract. The page takes the platform from `settings.platform` when the host sends one, else from `statsDirectHost.platform`, else from the bridge in use; it sets the class `host-<platform>` on the document element and chooses its wording by it.

## Actions, page to host (all required)

| Action | Payload |
|---|---|
| ready | hostContract |
| save | state (schemaVersion 2), revision |
| settings | none; the host shows its sign-in and answers with the settings callback |
| workspace | choice, or none to ask for the choices |
| ask | id, userMessageID, lesson, profile, stage, practiceContext, learningGoals, messages |
| cancel | id |
| help, example, r | action, lesson |
| copyCode | code: at most 100,000 units; the Mac counts UTF-8 bytes and Windows UTF-16 units, and a longer block is answered with a notice (to be made one rule at the next bump) |
| openRCode | generation, lessonID, messageID, blockIndex; the host opens the code and never runs it |
| export | format, record, text, html |
| email | recipient, record, text, html |
| captureLearningEvidence | documentID |
| importProviderReview | none |
| exampleCourse | none |
| importCoursePack | none |

An unknown action is to be answered with a notice, never dropped silently. On 6 November 2026 both hosts still drop it; the notice comes with the contract checks.

## Callbacks, host to page (all 19 required on both hosts)

| Callback | Payload the page reads |
|---|---|
| bridgeGeneration | the generation token, kept for openRCode |
| restore | the saved state, or null for a new record |
| loadError | message |
| settings | configured, disabled, service, label, signingIn; platform (optional) |
| recordSharingConsent | the consent entry; returns {state}, and throws before the record is loaded |
| workspace | choice, choices [{id, title, kind}], label; the page relies on the kinds report and r, other kinds are the host's |
| partial | id, text (ignored unless id is the pending reply) |
| resumeReply | action (reply or tutorError), value, partial; only after the record is loaded |
| diagnostics | an object stored in the record as sent |
| toolActivity | id, text (shown while id is the pending reply) |
| saved | the revision number saved |
| notice | message |
| reply | id, text, model, responseID, promptVersion, courseSources [{id, title, url?, retrievedAt?}], workspaceSources [strings], diagnostics (optional) |
| tutorError | id (optional), message, diagnostics (optional) |
| exportRecord | none |
| coursePack | the pack object, or null |
| providerReview | the review object |
| evidence | title, at, text |
| showOptions | none |

Windows implements `toolActivity` when it takes the package; the Mac calls all 19 today. A host calls no callback the page's declared contract lacks, and the page defines every one of these. `resources` is a Mac host call with no page member and is not part of the contract; it is to be removed.

## Order and semantics

- `bridgeGeneration` precedes `restore`; `resumeReply` comes only after the page has loaded; the other ready-time callbacks may come in any order (the two hosts order them differently).
- Stop: the page keeps `pending`, posts `cancel` and reports `tutorError` itself, so the learner's question and the streamed partial reply stay in the transcript with the stop notice. The host's own `tutorError` on cancellation is ignored once `pending` is clear. The Windows copy at origin/main (6 November 2026) clears `pending` first and removes the cancelled question; it adopts the page's semantics with the package.
- Ids: the assistant entry's id equals the request id; every entry carries `lessonId` except the welcome entry, which belongs to the epidemiology lesson by the page's and both hosts' fallback; `ask` carries `userMessageID`; `openRCode` carries the generation token. The host keeps R contexts by request id, rebuilds the script from the saved conversation filtered by `lessonId`, and never runs it.
- Recovery: the host retains the portfolio and the last reply event across renderer death and replays them.
- `lessons.json` fields a host reads: id, title, topic, operation, columns, r, help, providerCourse, contentVersion, knowledgeSchemaVersion, objective, summary, challenge, steps, review, contentSha256, difficulty, learnerGoals, learnerGoalNotes, prerequisites, learningObjectives, keyConcepts, misconceptions, workedExample, teachingPrompts, rProgression, sources, assessmentIds, practiceQuestions. The page's copy and the host's copy come from one publication.
- Element ids a host touches: `#loadError`, `#saveStatus`, `#optionSaved`, `#connectionPrivacy`.
- Learners see no provenance line in the lesson guide; the review record stays in the lesson data and the exports. Learning targets (difficulty, goals, notes) are shown.

## The bank module

`bank.mjs` is supplied by the host build, beside the shared modules or by a bundler remap. It exports `bankVersion`, `tracks` and `questions` (required) and `bankPublication`, the record of where the content came from (optional; `null` when the host keeps none).

## Tests

The shared tests take their inputs from `test-config.mjs`: `STATSDIRECT_CONTENT_ROOT` (the folder holding `Learn/lessons.json`, `Learn/provider-course-example.json`, `analysis-menu.json` and `Help/`), `STATSDIRECT_LEARN_ARCHIVE` (an archived question bank; the comparison is skipped where none exists and fails where a named one is missing) and `STATSDIRECT_RSCRIPT` (or the repository's `RSCRIPT`). R expressions are written to a temporary file and run with `Rscript --vanilla`; the C locale is set on Windows only.
