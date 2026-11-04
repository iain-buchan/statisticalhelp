# Tutor authoring for health and biomedicine

Draft design and source review, 3 November 2026. This is an authoring extension to the Help Editor. Existing StatsDirect learner records and course-pack import remain the delivery and evidence route.

## Start from the working tutor

The current Windows tutor imports provider courses using `schemaVersion: 2`: provider identity, version, readable supporting documents, ordered lessons, numeric worksheet examples, R scripts, HTTPS resources and fixed-key multiple-choice questions. The existing learning record retains question snapshots, first answers, assistance, practical submissions, reflection and imported provider responses. Its independent mode is unsupervised practice, not a secure examination. The editor should preserve these contracts rather than introduce a separate learning-management system.

Author workflow:

1. Open a copy of the basic material or clone a provider course. State the audience, intended level and learning objective.
2. Edit a lesson or question; add fictional data, a practical task, an R example and explanatory feedback. Preview the learner's choices and feedback without altering a real portfolio.
3. Ask ChatGPT to find sources, draft an exercise, or critique the current item. Include relevant course/help context and the intended audience. Show the research request and sources used.
4. Compare proposed changes with the current draft. Accept or reject explicitly. An AI answer is a proposal; source retrieval and a successful numerical check are separate evidence.
5. Check numerical results with a recorded R script/output, review assumptions and wording, then have a named human approve content. Record the item version and the scope/date of that approval.
6. Export a versioned course pack, import it through the existing StatsDirect Learning Options, and rehearse with tutors before distribution to students.

## Audience mapping

Use audience tags, not assertions that one item satisfies a whole examination or qualification.

| Audience | Initial emphasis | Primary orientation source |
|---|---|---|
| Undergraduate health and biomedical learners | Denominators, variation, study design, interpretation and uncertainty | Local course objectives plus the method's primary statistical documentation |
| Masters-level learners | Assumptions, modelling, confounding, reproducible R and critical appraisal | Local module outcomes; [CDC analysis/interpretation chapter](https://www.cdc.gov/field-epi-manual/php/chapters/analyze-interpret-data.html) |
| UK medical postgraduate learners | Risk measures, diagnostic tests, study design and communicating evidence | [MRCP Part 1 format](https://www.thefederation.uk/examinations/part-1/format), [RCGP AKT data interpretation topics](https://www.rcgp.org.uk/getmedia/dba5948c-3b9d-4936-918e-ca8cea1122b4/Data-Interpretation-topics-AKT.pdf) |
| US medical learners | Applied biostatistics and clinical epidemiology, tailored to the actual examination | [USMLE tasks/competencies](https://www.usmle.org/sites/default/files/2022-01/USMLE_Physician_Tasks_Competencies_2.pdf), [ABIM specialty blueprints](https://www.abim.org/about/abim-exams/blueprints/) |
| NHS commissioning and US payer analysts | Case-mix, denominators, service comparisons, missingness and defensible communication; transition from Excel to R | Locally agreed workforce objectives plus relevant methods sources |

USMLE is a licensing examination series, whereas ABIM is a board-certification body; do not label one as the other's blueprint. Each specialty, exam stage and jurisdiction needs its own mapping. MRCP/RCGP/USMLE/ABIM names identify intended preparation contexts, without implying affiliation or endorsement. Sources were opened on 3 November 2026; recheck changing curricula when publishing a course revision.

## Conversational question authoring and classification, 4 November 2026

Maintain the main bundled question library through its authoring course; optional provider course packs remain a separate export choice. Start with a concrete request such as: “Draft one beginner question about diagnostic-test denominators for undergraduate medicine, with one best answer, feedback for every choice, original fictional numbers and authoritative sources.” For an existing item, discuss its reasoning and alternatives before accepting the current proposed revision. The author should be able to refine the scenario, evidence, audience and difficulty in the same research conversation.

Keep three concepts separate. Difficulty describes the author's provisional estimate of statistical demand. Learner goals describe the intended educational or professional context, and one question may serve several goals. The existing learner pathway still controls the current application practice sets. Selecting a new audience label does not rewrite that pathway, create a new student filter, or establish examination alignment.

Question metadata use optional root fields, preserved in both built-in content and provider exports:

| Field | Meaning and values |
|---|---|
| `difficulty` | `beginner`, `intermediate` or `advanced`; leave unassigned when not yet judged. Treat this as an author estimate until tutor review and pilot experience support it. |
| `learnerGoals` | Up to eight distinct goal IDs from the table below. Multiple selections are intentional. |
| `learnerGoalNotes` | Up to 3,000 characters to state the local module, qualification stage, specialty, prerequisite knowledge or reason the question is suitable. |

| Goal ID | Author-facing context |
|---|---|
| `undergraduateMedicine` | Undergraduate medicine |
| `undergraduateHealthScience` | Undergraduate health sciences |
| `postgraduateMedicine` | Postgraduate medicine |
| `postgraduateAlliedHealth` | Postgraduate allied health |
| `publicHealth` | Public health |
| `healthcareBusinessAnalyst` | Healthcare business analyst |
| `mastersStatistics` | Master's-level statistics |
| `usMedicalBoards` | US medical board examinations |

Existing questions without these fields remain unassigned; there is no bulk inference from their original pathway. Missing or null values stay unassigned, an empty goal list is valid, and an empty difficulty string is invalid. A new question may start at beginner with no goals, but the author remains responsible for its classification. A single professional goal must not silently imply a particular difficulty. Use notes to identify a specific US board/specialty or UK qualification stage; broad goal labels do not claim endorsement, blueprint coverage, eligibility or examination readiness.

Research should return draft wording, a defensible key, explanations for the correct answer and distractors, relevant sources, and an inspectable R check when arithmetic is involved. Distinguish sources actually opened during the conversation from suggested URLs, and keep the “R not run” state until a check has been inspected and executed separately. Accepting an AI proposal makes it an editable draft; it does not mark its statistics or source claims as verified. Content and classification changes remain subject to the version's author-review requirements before publication.

These labels survive the complete question JSON, static built-in bank, provider conversion, learner attempt snapshots and JSON portfolio export. Existing readable learner reports enumerate selected fields and do not yet display the new classification metadata. That is a presentation limit rather than a loss of the stored question snapshot. This authoring change adds no adaptive selection, curriculum routing or learner-report redesign.

## Questions and practical exercises

Use original scenarios and data. Do not copy commercial question banks or present generated material as authentic examination questions. The [NBME item-writing resource](https://www.nbme.org/institutions/nbme-item-writing-guide/) is a useful author resource; its full guide may require registration. [ABIM describes clinical single-best-answer questions and a multi-stage review process](https://www.abim.org/about/abim-exams/exam-development-process/). The prototype does not reproduce that examination assurance process.

For each MCQ, check that the stem supplies enough information for one defensible best answer, distractors represent plausible misconceptions, and feedback explains both the key and alternatives. Avoid trivia, trick wording and irrelevant demographic detail. Keep mathematical direction, units, denominators, reference groups and follow-up periods explicit. Separate a statistical answer from a clinical or service recommendation. An audience tag cannot validate item difficulty; tutor review and pilot feedback are needed.

For practical work, ask for the selected method, assumptions, result, interpretation and a limitation. Make the R code self-contained and readable. Use native R functions where they answer the question; document extra code only when it adds educational or numerical value. A reproducible calculation does not validate causal inference, the clinical context or the entire explanation.

## Sources and review evidence

An authoring record should preserve each source's title, exact URL, access date, relevant claim, and how it was found. Distinguish a retrieved web source from a model-supplied citation that has not been opened. Prefer original methods papers, official documentation and authoritative teaching sources. If a source is inaccessible, report that state rather than inventing a quotation or implying verification. Let the author open sources and replace unsuitable references.

Suggested editorial states: draft; AI-assisted draft; numerical check recorded; human review needed; human approved. Keep the reviewer/date/scope and the R script, version and output together. Editing a previously approved item should invalidate the approval for that edited version. Do not silently overwrite historical question snapshots. Preserve unknown course-pack fields so providers can retain their own metadata.

## Learning evidence and CPD

[GMC CPD guidance](https://www.gmc-uk.org/education/standards-guidance-and-curricula/guidance/continuing-professional-development) emphasises planning, learning needs, evaluation and recording impact. The authoring form can therefore add prompts for the learner's objective, interpretation, reflection, intended application and a later example of impact. Existing portfolio export is the appropriate evidence route.

A score, elapsed time or AI conversation alone must not be labelled an awarded credit. Recognition, hours/credits and assessment conditions belong to the relevant provider or professional scheme. Imported review files remain unauthenticated unless an independently implemented verification route establishes otherwise. For the proposed Liverpool/Manchester workforce rehearsal, agree locally which outputs tutors will review and what evidence the organisation will accept; this prototype makes no accreditation claim.

## Initial original draft pack

`TutorExamples/health-biomed-draft-pack.json` contains four short lessons and four original MCQs: diagnostic conditional denominators, absolute treatment benefit, paired measurements, and service case-mix. All numbers and scenarios are fictional. `TutorExamples/verify-health-biomed-exercises.R` checks the numerical answers using base R 4.6.2; output is recorded in `evidence/tutor-health-biomed-r-checks.txt`. The clinical and educational wording still needs the course tutor's approval. Nothing in this pack has been published to the main application.

## Prototype acceptance checks

- Baseline material loads without changing original tutor sources or saved learner records.
- Add, edit, clone, reorder and recover a deleted item; save/reopen without losing IDs, versions, answer keys, feedback or provider metadata.
- Invalid JSON, duplicate IDs, missing keys, unsupported content and broken local help references are reported before export.
- Preview marks answers correctly, keeps the key hidden until the learner reveals it, and never writes to a live portfolio.
- Research cancellation, sign-in/usage failures, empty results and malformed AI proposals preserve the draft. Accepted changes are reviewable and reversible.
- Exported provider courses pass the real Windows course-pack validator and retain sources/review metadata as optional fields.
- No website output, release installer, main app content or learner portfolio is silently replaced.

## Independent prototype review, 3 November 2026

The implemented authoring path was reviewed against the real Windows course-pack contract. Review findings led to preserved AI source/review evidence on acceptance; read-only AI access to course readings and objectives; editing/preview of the existing richer lesson fields; a named author-review action; invalidation of older numerical-check claims after content changes; and distinct version identities for different unsaved exports. Active table-cell edits are flushed before saving, including answer choices, reference details and teaching prompts.

Native checks passed 35 assertions and Core checks passed 45. Independent browser checks passed 15 assertions on HTML generated by the native form: answer-key behaviour, choice-specific feedback, hidden feedback before answering, literal HTML-like authored text, readable R code with line breaks, narrow-pane layout and no external requests or execution. These checks do not certify the scientific accuracy of arbitrary material an author enters.

One bounded live ChatGPT research example was also inspected independently. It used the [CDC surveillance-classification figure](https://archive.cdc.gov/www_cdc_gov/csels/dsepd/ss1978/lesson5/appendixa.html), explicitly distinguishing its source context from a fictional screening question. Its proposed arithmetic-only R check was inspected and run manually: sensitivity 80%, false-negative proportion 20%, PPV 47.05882% and specificity 90%. This was a developer verification, not automatic execution by the application and not acceptance/publication of the proposed item. Evidence is in `evidence/tutor-live-proposal-r-check.R`, `evidence/tutor-live-proposal-r-result.txt`, `evidence/TutorPreviewBrowserChecks-20261103/checks.json` and `evidence/tutor-education-product-review.json`.
