# Editable tutor course source

`starter-course.json` is an independent working copy of StatsDirect's current
12 lessons and 35 original draft multiple-choice questions. It includes the
original numerical examples, base-R scripts, objectives, misconceptions,
feedback and editorial references. Original upstream files are retained in
`Baseline`; `provenance.json` records their hashes and source revision. These
MIT-licensed teaching drafts are not official examination items.

Open the working copy in the Help Editor's tutor authoring window. Add or revise
lessons, exercises, source notes and MCQs; keep one best answer and explain the
distractors. Use the research/review workflow to gather sources and check the
statistical reasoning before recording an author review. Material can remain an
incomplete saved draft; it must pass runtime validation before export.

For maintaining the main bundled StatsDirect tutor, save a draft, record your
checks, then choose **File > Publish built-in tutor for StatsDirect builds…**.
The selected StatsDirect repository receives `Learning/AuthoredTutor` source
files. Its normal Windows build validates the manifest and builds the authored
lessons and question bank into both the web interface and native lesson context.
New or changed records require a review of their exact content; stale numerical
checks must be renewed with actual evidence. The original five pathway IDs and
question identities are preserved. Publishing does not push to GitHub or build
installers. The immutable packaged starter is separate from this working copy.

The following provider-course export is a secondary option for local courses.

Export a separate JSON course pack, then import it in StatsDirect through
**Learning Options > Import course materials**. Current Windows and Mac tutor
formats add the personalised course alongside the built-in material. They do
not replace the bundled library. R scripts are supplied for the learner to open
and review; import does not run them. A local preview does not award CPD credit.

Course identity is `id` plus `version`. An edit followed by Save advances the
version with a `-rN` suffix unless the author explicitly sets a different version.
An export containing unsaved changes uses a deterministic content suffix, so
different exported drafts cannot silently reuse the same course question key.
Question edits advance their own version and reset review to Draft. Review-only
changes preserve review status and the question version. This keeps old learner
attempt snapshots distinct from newly imported course content.

Deleted lessons/questions/source notes are retained under the local `authoring`
metadata and can be restored after reopening the file. Runtime export omits this
metadata and its recovery records. The working file should therefore be kept;
an exported pack is not a substitute for the authoring source.
Authoring files support up to 16 MB for local research and recovery data; the
exported runtime course retains StatsDirect's 2 MB limit. Limits report an error
without truncating content.

`Import-StatsDirect.mjs <StatsDirect repository>` reproduces the initial import
from a trusted source checkout. It refuses to overwrite the editable starter.
It reads upstream lesson JSON and its data-only question bank without modifying
the original source. The software and source material retain `LICENSE`.
