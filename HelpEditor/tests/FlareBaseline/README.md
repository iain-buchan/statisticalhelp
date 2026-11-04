# Frozen Flare comparison fixture

This is the last original **1 November 2026** Flare output for `DesktopHelp`, `Website` and `GitHub` (Flare 22.0.9643.36696). It survives retirement of Flare and does not depend on a developer's `C:\Develop` directories.

`flare-20261101.tar.gz` is 17,515,910 bytes. `manifest.json` pins its SHA-256, every reader file's size/hash, all **404 authored topic paths** in each target, and provenance. The 2,964 reader files retain their original bytes, including navigation, search, fonts, images, context aliases and all 258 equation SVGs per target. Extra Flare runtime HTML pages are retained. Only the three `.mclog` build logs were excluded; no authoring profiles, credentials, destinations or recovery archives were captured.

The baseline deliberately contains the seven approved **source-corrected equation pictures**, rather than their older published-site versions. Their prior/current hashes are recorded in the manifest. The existing 79 exact bibliography corrections are preserved separately in `reference-link-corrections.json`, with a pinned hash. Comparisons still validate each old/new reference anchor, URL and text exactly; this fixture adds no tolerance and hides no new difference.

The comparison's existing `rates/rates.htm` text/link difference and the added **Incidence and prevalence** Contents entry remain visible in its report. The path change does not add or widen either allowance: all 404 titles, R blocks and non-equation images are checked in every target, and unexpected differences still fail.

Run the integrity gate with Node only:

```text
node tests/FlareBaselineIntegrityChecks.cjs
```

`tests/flare-baseline.cjs` validates the archive and extracts it to ignored `evidence/FlareBaselineCache/<archive-sha256>`. It uses only Node's filesystem, crypto and gzip built-ins. Before extraction, every archive header/path/content hash is checked; symbolic links, unexpected entries and path escapes are rejected. Every reuse verifies the complete extracted inventory. No browser, .NET SDK, Flare, npm archive package or network download is needed for extraction.

Comparison scripts use `resolveFlareRoot()` or `resolveFlareTarget(name)`. CI, or `STATSDIRECT_REQUIRE_FROZEN_FLARE=1`, always uses this frozen fixture and fails if it is missing or corrupt. An installed/local Flare output is never a CI fallback. Outside CI, explicit `STATSDIRECT_FLARE_OUTPUT`, `STATSDIRECT_FLARE_WEBSITE`, `STATSDIRECT_FLARE_DESKTOP` and `STATSDIRECT_FLARE_GITHUB` overrides remain available for deliberate development comparisons. The old development path is used only when the frozen fixture is absent, with a visible warning. A partially missing or corrupt fixture fails rather than falling back.

`tests/FreezeFlareBaseline.cjs <absolute original Output root>` records a baseline only when these fixture files do not already exist. It is not invoked by CI or comparisons. Replacing this historical baseline, its archive pin or its correction records requires an explicitly reviewed source change; never refresh it simply to make a comparison pass.
