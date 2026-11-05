[CmdletBinding()]
param(
    [ValidateSet('Authoring', 'Output', 'Parity', 'All')]
    [string]$Group = 'All'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$reports = Join-Path $root ('evidence\BrowserRegression-' + $Group)
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$node = (Get-Command node -ErrorAction Stop).Source
$dependencyRoot = Join-Path $PSScriptRoot 'browser\node_modules'
$oldPlaywright = $env:STATSDIRECT_PLAYWRIGHT_PATH
$oldNodePath = $env:NODE_PATH
$oldBuild = $env:STATSDIRECT_HELP_BUILD
$results = [Collections.Generic.List[object]]::new()
$runnerError = $null

function Run-Browser([string]$File, [string]$Name = $File, [string[]]$ScriptArguments = @()) {
    $log = Join-Path $reports ($Name + '.log')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $node (Join-Path $PSScriptRoot $File) @ScriptArguments *> $log
    $code = $LASTEXITCODE
    # Some old browser consumers record a failed assertion without setting exit 1.
    # Treat such evidence as a failure too; keep the complete output for diagnosis.
    $failed = @(Select-String -LiteralPath $log -Pattern '"passed"\s*:\s*false').Count
    $results.Add([pscustomobject]@{
        name = $Name; script = $File; exitCode = $code
        failedMarkers = $failed; seconds = $watch.Elapsed.TotalSeconds
    })
    Write-Host ($Name + ': ' + $(if ($code -eq 0 -and $failed -eq 0) { 'PASS' } else { 'FAIL' }))
}

Push-Location $root
try {
    if (Test-Path -LiteralPath (Join-Path $dependencyRoot 'playwright')) {
        $env:STATSDIRECT_PLAYWRIGHT_PATH = Join-Path $dependencyRoot 'playwright'
        $env:NODE_PATH = $dependencyRoot
    }
    if ($Group -in @('Authoring', 'All')) {
        foreach ($file in @(
            'web-checks.cjs', 'math-keyboard-checks.cjs', 'math-menu-checks.cjs',
            'web-edit-lifecycle-checks.cjs', 'link-ribbon-checks.cjs',
            'LinkFormattingPreservationChecks.cjs', 'workspace-view-checks.cjs', 'BrowserHandoffChecks.cjs', 'SourceContentBrowserChecks.cjs',
            'preview-link-checks.cjs', 'viewer-skin-checks.cjs',
            'deep-authoring-checks.cjs', 'overnight-browser-workflows.cjs',
            'FormattedEquationOverflowChecks.cjs', 'web-namespace-checks.cjs'
        )) { Run-Browser $file }
    }
    if ($Group -in @('Output', 'All')) {
        foreach ($target in @('Desktop', 'Public')) {
            $env:STATSDIRECT_HELP_BUILD = Join-Path $root ('Build\' + $target)
            Run-Browser 'BuilderBrowserChecks.cjs' ('AllPages-' + $target)
            Run-Browser 'preview-style-checks.cjs' ('PreviewStyle-' + $target)
        }
        $env:STATSDIRECT_HELP_BUILD = $oldBuild
        foreach ($file in @(
            'output-link-checks.cjs', 'EquationRowChecks.cjs',
            'EquationOverflowChecks.cjs', 'EquationArtworkFallbackChecks.cjs',
            'SourceConditionChecks.cjs', 'search-visibility-checks.cjs',
            'FileBrowserPreviewChecks.cjs'
        )) { Run-Browser $file }
        $zipInput = Join-Path $root 'evidence\WebsiteZipFeature\draft-reader-input.json'
        if (-not (Test-Path -LiteralPath $zipInput)) { throw 'Run WebsiteZipChecks before the output browser group so its fresh exported ZIP can be independently read.' }
        $zip = Get-Content -LiteralPath $zipInput -Raw | ConvertFrom-Json
        Run-Browser 'WebsiteZipReaderChecks.cjs' 'WebsiteZipReader' @('--zip', $zip.zip, '--expect', $zipInput, '--source', (Join-Path $root 'HelpSource\Content'), '--evidence', (Join-Path $reports ('WebsiteZipReader-' + [Guid]::NewGuid().ToString('N'))))
    }
    if ($Group -in @('Parity', 'All')) {
        foreach ($file in @(
            'compare-flare-targets.cjs', 'VisualFidelityChecks.cjs',
            'EquationFidelityChecks.cjs', 'FlareAppearanceChecks.cjs',
            'FlareEquationAppearanceChecks.cjs', 'EquationArtworkFlowChecks.cjs',
            'FlareNavigationStateChecks.cjs', 'FlarePrintChecks.cjs'
        )) { Run-Browser $file }
    }
}
catch {
    $runnerError = $_.Exception.Message
    throw
}
finally {
    $env:STATSDIRECT_PLAYWRIGHT_PATH = $oldPlaywright
    $env:NODE_PATH = $oldNodePath
    $env:STATSDIRECT_HELP_BUILD = $oldBuild
    Pop-Location
    $failedRuns = @($results | Where-Object { $_.exitCode -ne 0 -or $_.failedMarkers -ne 0 })
    [ordered]@{
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        group = $Group; passed = $null -eq $runnerError -and $results.Count -gt 0 -and $failedRuns.Count -eq 0
        infrastructureError = $runnerError
        runs = @($results)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reports 'results.json') -Encoding utf8
}
if ($failedRuns.Count) { throw 'Browser regression checks failed; inspect the recorded evidence.' }
