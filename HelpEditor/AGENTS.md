# Help Editor development

Work inside this directory. The supplied `HelpSource` is an independent copy.
Keep the repository-root Content/Project/Docs and published help unchanged
unless the user explicitly asks to update them. Never publish Flare destinations.

After editing saved help source, rebuild it yourself with
`./Build-Help.ps1 -Target All`. Do not ask the user to run Flare. The packaged
console builder needs no GUI or SDK for routine source rebuilds. After changing
the builder's C# code, package it with `-CompileBuilder` (SDK required).

Use `App/Builder/StatsDirectHelpBuilder.exe export-website --project <working-copy>
--zip <destination.zip>` to make a fresh Website archive from saved source. It
puts website files at the ZIP root and uploads nothing. The GUI can also include
the current unsaved draft. Its last successful save destination is local state
in `Settings/authoring-preferences.json`; never package or commit that file.

Tests must use isolated fixtures and preserve source, recovery archives and
unsaved GUI work. Do not commit profiles, login state, private signing material,
generated builds, test fixtures or recovery copies. GitHub publishing and
installer release are explicit operations. See README and installer/README.md.
