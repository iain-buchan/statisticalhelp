param([switch]$CompareFlare,[string]$StatsDirectSourceRoot)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$previousPlaywright=$env:STATSDIRECT_PLAYWRIGHT_PATH
$previousNodePath=$env:NODE_PATH
$browserDependencies=Join-Path $root 'tests\browser\node_modules'
if(Test-Path -LiteralPath (Join-Path $browserDependencies 'playwright')) {
    $env:STATSDIRECT_PLAYWRIGHT_PATH=Join-Path $browserDependencies 'playwright'
    $env:NODE_PATH=$browserDependencies
}
$node=Get-Command node -ErrorAction SilentlyContinue
if(-not $node){throw 'Node.js is required for developer browser checks (not for using the editor).'}
function Run-Check([string]$Name,[scriptblock]$Action){
    Write-Host $Name
    & $Action
    if($LASTEXITCODE -ne 0){throw ($Name+' failed with exit code '+$LASTEXITCODE)}
}
Push-Location $root
try{
    Run-Check 'Build solution' {dotnet build StatsDirectHelpEditor.slnx --configuration Release --nologo --verbosity minimal}
    foreach($name in @('ContentsNavigationChecks','DesktopDeliveryChecks','PortabilityChecks','DeliveryWindowsChecks','HelpPublicationChecks','HelpPublicationWindowsChecks','NestedGitChecks')) {
        Run-Check $name {dotnet run --no-build --project ('tests/StatsDirectHelpEditor.'+$name) --configuration Release -- $root}
    }
    foreach($name in @('Checks','WindowsChecks','ContentsWindowsChecks','LinkWindowsChecks','BrowserPreviewWindowsChecks','WebsiteExportWindowsChecks','WorkspaceWindowsChecks','AppHelpPreviewChecks','SearchReplaceChecks','SearchReplaceWindowsChecks','SourceImageWindowsChecks','InstalledWorkspaceChecks','InstalledWorkspaceWindowsChecks','PolishChecks','PolishTransportChecks','PolishWindowsChecks','PreviewCacheWindowsChecks','DeepPreviewChecks','DeepSourceChecks','DeepWorkflowWindowsChecks','GitChecks','WebsitePublishChecks','HeadlessChecks','EquationArtworkChecks','CssScoperChecks')){
        $project='tests/StatsDirectHelpEditor.'+$name
        Run-Check $name {dotnet run --no-build --project $project --configuration Release -- $root}
    }
    Run-Check 'Website ZIP Core and CLI export' {dotnet run --no-build --project tests/StatsDirectHelpEditor.WebsiteZipChecks --configuration Release -- $root}
    Run-Check 'Tutor course authoring and exact runtime import' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorAuthoringChecks --configuration Release -- $root}
    Run-Check 'Built-in tutor publishing and review gates' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorBuiltinChecks --configuration Release -- $root}
    Run-Check 'Tutor native editing and preview fixtures' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorAuthoringWindowsChecks --configuration Release -- $root}
    Run-Check 'Tutor embedded WebView2 learner preview' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorAuthoringWindowsChecks --configuration Release -- $root --preview-only}
    Run-Check 'Tutor research protocol and context' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorResearchChecks --configuration Release -- $root}
    Run-Check 'Tutor research dialog' {dotnet run --no-build --project tests/StatsDirectHelpEditor.TutorResearchChecks --configuration Release -- $root --dialog}
    Run-Check 'Tutor learner-content preview' {& $node.Source tests/TutorPreviewChecks.cjs}
    if($StatsDirectSourceRoot){Run-Check 'Tutor normal Windows MSBuild hook' {& (Join-Path $root 'tests/TutorBuildHookChecks.ps1') -StatsDirectSourceRoot $StatsDirectSourceRoot}}
    Run-Check 'Deleted-page recovery' {dotnet run --no-build --project tests/StatsDirectHelpEditor.Checks --configuration Release -- $root --recovery-only}
    & (Join-Path $root 'Build-Help.ps1') -CompileBuilder
    Run-Check 'Headless wrapper without SDK or Flare' { & (Join-Path $root 'tests/headless-wrapper-checks.ps1') -Root $root }
    foreach($file in @('web-checks.cjs','math-keyboard-checks.cjs','math-menu-checks.cjs','web-edit-lifecycle-checks.cjs','link-ribbon-checks.cjs','LinkFormattingPreservationChecks.cjs','workspace-view-checks.cjs','preview-link-checks.cjs','viewer-skin-checks.cjs','deep-authoring-checks.cjs')){
        if(Test-Path -LiteralPath (Join-Path $root ('tests\'+$file))){Run-Check $file {& $node.Source ('tests/'+$file)}}
    }
    $previousBuild=$env:STATSDIRECT_HELP_BUILD
    try{
        foreach($target in @('Desktop','Public')){
            $env:STATSDIRECT_HELP_BUILD=Join-Path $root ('Build\'+$target)
            Run-Check ('Browse all '+$target+' help pages') {& $node.Source tests/BuilderBrowserChecks.cjs}
            Run-Check ($target+' draft preview matches generated page styles') {& $node.Source tests/preview-style-checks.cjs}
        }
    }finally{$env:STATSDIRECT_HELP_BUILD=$previousBuild}
    Run-Check 'All output links and anchors' {& $node.Source tests/output-link-checks.cjs}
    Run-Check 'Multiline equation layout' {& $node.Source tests/EquationRowChecks.cjs}
    Run-Check 'Equation overflow and complete SVG visibility' {& $node.Source tests/EquationOverflowChecks.cjs}
    Run-Check 'Original, edited and new equation artwork through preview and both builds' {& $node.Source tests/EquationArtworkFallbackChecks.cjs}
    Run-Check 'Oversized original equations preserve surrounding text flow' {& $node.Source tests/EquationArtworkFlowChecks.cjs}
    Run-Check 'Desktop/Public source conditions' {& $node.Source tests/SourceConditionChecks.cjs}
    Run-Check 'Help search navigation' {& $node.Source tests/StatsDirectHelpEditor.Checks/ViewerNavigationChecks.cjs}
    Run-Check 'External reference links' {& $node.Source tests/StatsDirectHelpEditor.Checks/ExternalLinkChecks.cjs}
    Run-Check 'Whole-site preview from local files' {& $node.Source tests/FileBrowserPreviewChecks.cjs}
    if($CompareFlare){
        Run-Check 'Compare all three existing Flare targets' {& $node.Source tests/compare-flare-targets.cjs}
        Run-Check 'Compare rendered Flare typography and artwork' {& $node.Source tests/VisualFidelityChecks.cjs}
        Run-Check 'Compare all rendered equations with Flare' {& $node.Source tests/EquationFidelityChecks.cjs}
        Run-Check 'Compare complete Flare appearance across screen sizes' {& $node.Source tests/FlareAppearanceChecks.cjs}
        Run-Check 'Compare all original equation artwork pixels with Flare' {& $node.Source tests/FlareEquationAppearanceChecks.cjs}
        Run-Check 'Print complete topics with navigation open or closed' {& $node.Source tests/FlarePrintChecks.cjs}
    }
    Write-Host 'All requested checks passed. Detailed reports are in evidence.'
}finally{Pop-Location;$env:STATSDIRECT_PLAYWRIGHT_PATH=$previousPlaywright;$env:NODE_PATH=$previousNodePath}
