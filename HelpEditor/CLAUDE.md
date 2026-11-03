# Help Editor

Read AGENTS.md. Rebuild saved help changes yourself with
`./Build-Help.ps1 -Target All`; no Flare GUI is needed. Use `-CompileBuilder`
only after changing builder code. Output stays local until publication is
explicitly requested. Preserve drafts and recovery archives; use test fixtures.

The headless builder also supports `export-website --project <working-copy>
--zip <destination.zip>` for a fresh website transfer ZIP. It uses saved source,
places website files at the archive root, and uploads nothing. GUI export can
include the current unsaved draft and remembers the last successful path locally.
