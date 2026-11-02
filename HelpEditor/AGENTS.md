# Help Editor development

Work inside this directory. The supplied `HelpSource` is an independent copy.
Keep the repository's canonical Content/Project/Docs and published help unchanged
unless the user explicitly asks to update them. Never publish Flare destinations.

After editing saved help source, rebuild it yourself with
`./Build-Help.ps1 -Target All`. Do not ask the user to run Flare. The packaged
console builder needs no GUI or SDK for routine source rebuilds. After changing
the builder's C# code, package it with `-CompileBuilder` (SDK required).

Tests must use isolated fixtures and preserve source, recovery archives and
unsaved GUI work. Do not commit profiles, login state, private signing material,
generated builds, test fixtures or recovery copies. GitHub publishing and
installer release are explicit operations. See README and installer/README.md.
