# Desktop help delivery

## One operation for a help release

Use **File > Publish help…** to produce the website ZIP, the GitHub source/website commit and the desktop-help copy together. On first use, set the GitHub HTTPS repository and branch, author identity, website ZIP filename and StatsDirect source path. These choices are remembered after successful completion. The dialog reviews the actual desktop destination and existing file hash; adoption of existing Flare output is explicit.

All three outputs come from one captured saved-source/runtime snapshot. Public and Desktop are built once each. The website ZIP and root GitHub `Docs` share the same Public reader files; desktop output has its own target conditions and a matching snapshot identifier. A private checkout carries the source and generated website commit without rewriting the author's original source or an unrelated checkout. The maintained `HelpEditor/HelpSource` layout is preferred when present; a legacy repository-root help source is also supported.

The network push happens only after local outputs and receipts verify. Publication to the default branch is a separate explicit choice; publishing a working branch does not merge it or make the live site current. Even a successful default-branch push reports GitHub Pages deployment as unverified/asynchronous. Uploading the ZIP to statsdirect.com is left to the author.

There is no atomic transaction spanning a remote server and multiple filesystems. A failed operation reports per-target states and keeps its private journal and snapshot for inspection; it does not claim completion or roll back a successful remote publication. Correct the reported problem and retry from saved source; retained earlier receipts identify which snapshot each existing target contains. Desktop interruption recovery below still applies.

For the same workflow without the GUI:

```powershell
 $builder = 'C:\Develop\StatsDirectHelp\App\Builder\StatsDirectHelpBuilder.exe'
& $builder publish-help --project '<marked saved help source>' `
    --zip '<website.zip>' --github-url 'https://github.com/owner/statisticalhelp' `
    --github-branch master --author-name '<name>' --author-email '<email>' `
    --statsdirect-source '<StatsDirect repository>' `
    --expected-destination-sha256 '<inspected desktop hash>' --allow-default-branch
```

Use `--adopt-flare-output --adoption-reason '<review notes>'` on the first reviewed Flare replacement. `--prepare-only` deliberately leaves GitHub unpushed and returns an incomplete publication receipt; it is not a completed live publication. Publication history is private local state. Ordinary GitHub-menu pushes continue to protect the default branch.

The working `HelpSource` remains separate from the original Flare tree. Ordinary builds write evaluation output only. The explicit desktop publishing command is the route into a selected StatsDirect source repository; it does not compile the application, sign anything or build installers.

## Author workflow

1. Save page edits and resolve any open hyperlink or equation draft.
2. Check Browser view and App view, or rebuild both targets without the GUI.
3. Choose **File > Publish desktop help for StatsDirect builds…**, select the intended StatsDirect repository and review the destination and inspected hash. The first replacement of recognisable Flare output records explicit adoption.
4. Build StatsDirect only after publication finishes. Keep publication and application builds sequential.

The destination must have a recognisable StatsDirect project identity and is fixed to `StatsDirectUI/Assets/Help`. No arbitrary destination or unknown output is silently adopted. Complete generated-file hashes, entry page, search documents and context aliases are checked before activation and again afterwards. Existing output changes after inspection are conflicts.

## Command line

From a deployed editor, use `App/Builder/StatsDirectHelpBuilder.exe`:

```powershell
$builder = 'C:\Develop\StatsDirectHelp\App\Builder\StatsDirectHelpBuilder.exe'
$repo = '<selected StatsDirect source repository>'
& $builder inspect-desktop-help --statsdirect-source $repo
& $builder publish-desktop-help --project '<marked help working copy>' `
    --statsdirect-source $repo --expected-destination-sha256 '<inspection Sha256>' `
    --adopt-flare-output --adoption-reason 'Reviewed replacement of the previous Flare help'
& $builder verify-desktop-help --statsdirect-source $repo
```

Use the adoption options only when inspecting recognisable Flare output. Repeat deliveries still require the current inspected hash. Build warnings block publication unless deliberately reviewed with `--allow-warnings`. Successful commands emit JSON; failures return exit code 1. Compare saved sources with `compare-source --left <source> --right <source>`.

## Interruption and build guard

Publication uses verified staging and a durable repository-root `.statsdirect-desktop-help.publishing.json` journal. If interrupted, do not build or manually copy files. Run:

```powershell
& $builder recover-desktop-help --statsdirect-source $repo
& $builder verify-desktop-help --statsdirect-source $repo
```

Recovery chooses only a recorded, complete verified tree and refuses unexpected changes. If it restores the previous Flare tree, inspect and republish before expecting an editor delivery receipt. Recovery does not erase unrelated files. Transactional previous copies are removed after completion; they are not retained rollback builds.

The publisher does **not** modify the main application's project file. Its optional `DesktopHelpPublisher.ConsumerGuardXml` can be saved as `BuildTools/StatsDirectDesktopHelp.targets` and explicitly imported from `StatsDirectUI.csproj`. This blocks a build with an existing incomplete-publication marker. An unmodified application build does not read that marker. It is a pending-publication guard, not a concurrent-build lock or full receipt verifier; keep publishing and application builds sequential and verify before release. This integration is tested in an isolated copy, not installed into the live main repository by the editor.

## Source protection and power failure

Additional protected folders are private machine settings at `%LOCALAPPDATA%/StatsDirect/HelpEditor/Settings/workspace-protection.json`, not personal paths built into the public program. An optional `STATSDIRECT_HELP_PROTECTION_CONFIG` overrides the file for isolated testing. The file contains `schemaVersion: 1` and an array of absolute `protectedPaths`. Invalid existing settings fail closed. These restrictions protect ordinary editing/build output; explicitly reviewed desktop delivery is a separate operation with its own ownership checks.

Page-recovery archives must be kept. Search-and-replace checks and restores ordinary process errors, but its multi-file write is not a power-loss transaction: a power failure can leave a partially changed batch. Commit reviewed source regularly and retain the working copy and recovery data. Output staging and desktop delivery do not make unsaved editing or every multi-page edit power-failure-proof.
