[CmdletBinding()]
param(
    [ValidateSet('Desktop','Public','Both','All','DesktopHelp','Website','GitHub')]
    [string]$Target = 'Both',
    [ValidateNotNullOrEmpty()][string]$ProjectPath = (Join-Path $PSScriptRoot 'HelpSource'),
    [ValidateNotNullOrEmpty()][string]$OutputRoot = (Join-Path $PSScriptRoot 'Build'),
    [ValidateNotNullOrEmpty()][string]$AssetsPath = (Join-Path $PSScriptRoot 'App\Web'),
    [switch]$CompileBuilder
)
$ErrorActionPreference = 'Stop'

# Routine XHTML builds use this console app and the installed .NET runtime.
# The SDK is needed only when explicitly rebuilding the builder's C# code.
$installed = Test-Path -LiteralPath (Join-Path $PSScriptRoot '.statsdirect-help-editor-install.json') -PathType Leaf
$builder = Join-Path $PSScriptRoot $(if ($installed) { 'Builder\StatsDirectHelpBuilder.exe' } else { 'App\Builder\StatsDirectHelpBuilder.exe' })
if ($CompileBuilder) {
    if ($installed) { throw 'The installed editor already includes its builder. To compile C# changes, use a source checkout.' }
    $builderProject = Join-Path $PSScriptRoot 'src\StatsDirectHelpEditor.Cli\StatsDirectHelpEditor.Cli.csproj'
    & dotnet publish $builderProject --configuration Release --no-self-contained --output (Split-Path -Parent $builder) --nologo --verbosity minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The command-line help builder did not compile.' }
}
if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) {
    if ($installed) { throw 'The installed help builder is missing. Repair the Help Editor installation.' }
    throw 'App\Builder\StatsDirectHelpBuilder.exe is missing. On a development PC, run Build-Help.ps1 -CompileBuilder once to package it.'
}
if ($installed) {
    if (-not $PSBoundParameters.ContainsKey('AssetsPath')) { $AssetsPath = Join-Path $PSScriptRoot 'Web' }
    if (-not $PSBoundParameters.ContainsKey('ProjectPath') -or -not $PSBoundParameters.ContainsKey('OutputRoot')) {
        $workspaceJson = & $builder prepare-workspace
        if ($LASTEXITCODE -ne 0) { throw 'The installed help workspace could not be prepared.' }
        $workspace = ($workspaceJson -join [Environment]::NewLine) | ConvertFrom-Json
        if (-not $PSBoundParameters.ContainsKey('ProjectPath')) { $ProjectPath = $workspace.ProjectPath }
        if (-not $PSBoundParameters.ContainsKey('OutputRoot')) { $OutputRoot = Join-Path $workspace.WorkspaceRoot 'Build' }
    }
}
elseif (-not $PSBoundParameters.ContainsKey('AssetsPath') -and -not (Test-Path -LiteralPath $AssetsPath -PathType Container)) {
    # A clean source checkout can package/rebuild its console builder before
    # anyone publishes or starts the GUI. Use its checked-in offline assets.
    $AssetsPath = Join-Path $PSScriptRoot 'src\StatsDirectHelpEditor.App\Web'
}

$ProjectPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ProjectPath)
$OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)
$AssetsPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AssetsPath)
$targets = switch ($Target) {
    { $_ -in 'Both','All' } { @('DesktopHelp','Website'); break }
    { $_ -in 'Desktop','DesktopHelp' } { @('DesktopHelp'); break }
    'GitHub' { @('GitHub'); break }
    default { @('Website') }
}
$results = @()
foreach ($name in $targets) {
    $folder = if ($name -eq 'DesktopHelp') { 'Desktop' } else { 'Public' }
    $builderArguments = @('build','--project',$ProjectPath,'--output',(Join-Path $OutputRoot $folder),'--assets',$AssetsPath,'--target',$name)
    $json = & $builder @builderArguments
    if ($LASTEXITCODE -ne 0) { throw ('Help build failed: ' + $name + '. See the builder error above. Nothing was published.') }
    $result = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    [string[]]$consumerTargets = @('DesktopHelp')
    if ($folder -eq 'Public') { $consumerTargets = @('Website','GitHub') }
    $result | Add-Member -NotePropertyName ConsumerTargets -NotePropertyValue $consumerTargets
    $results += $result
}

[pscustomobject]@{
    Builder = $builder
    SourceProject = $ProjectPath
    BuiltAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Outputs = @($results)
    Published = $false
} | ConvertTo-Json -Depth 7
