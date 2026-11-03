# Tutor authoring compatibility checks

The two `Runtime*.cs` files are byte-identical snapshots of StatsDirect's current
Windows course-pack validator and JSON helpers (`StatsDirectUI/UI/Learning`),
MIT licensed, copyright Prof Iain E. Buchan. They compile only into this test
program. Running the exact runtime validator against exported JSON tests the
actual existing import contract rather than a second invented schema.

Snapshot source: `https://github.com/iain-buchan/statsdirect`, commit
`95e23818` (local source checked 3 November 2026). Their SHA256 values are
recorded by each run. No main-app files are changed or linked at build time.

Run `dotnet run -c Release --project tests/StatsDirectHelpEditor.TutorAuthoringChecks -- <HelpEditor root>`.
