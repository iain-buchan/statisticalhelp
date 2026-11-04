#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$BuilderPath = (Join-Path $PSScriptRoot '..\src\StatsDirectHelpEditor.Cli\bin\Release\net10.0\StatsDirectHelpBuilder.exe'),
    [string]$AssetsPath = (Join-Path $PSScriptRoot '..\src\StatsDirectHelpEditor.App\Web')
)

# Current authoring content is validated dynamically. Historical Flare/corpus
# comparisons belong in Prepare-RegressionWorkspace, not against these inputs.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectPath)
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh validation output directory.' }
$builder = [IO.Path]::GetFullPath($BuilderPath)
$assets = [IO.Path]::GetFullPath($AssetsPath)
if ($output.StartsWith($project.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Validation output must be outside the maintained source.'
}
New-Item -ItemType Directory -Path $output | Out-Null
function Digest([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
function Inventory([string]$Directory) {
    $result = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force) { $result[$file.FullName] = Digest $file.FullName }
    return $result
}
function Read-Xml([string]$File) {
    $settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null; $settings.MaxCharactersInDocument = 20000000
    $reader = [Xml.XmlReader]::Create($File, $settings)
    try { return [Xml.Linq.XDocument]::Load($reader, [Xml.Linq.LoadOptions]::PreserveWhitespace) }
    finally { $reader.Dispose() }
}
function Run-Tool([string]$Executable, [string[]]$Arguments, [string]$LogName, [hashtable]$Environment = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) { $process.Kill($true); throw ('Validation timed out: ' + $LogName) }
        $text = $stdout.GetAwaiter().GetResult(); $errors = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $output ($LogName + '.log')), $text + $errors)
        if ($process.ExitCode -ne 0) { throw ('Validation failed: ' + $LogName + ' (see its log).') }
        return $text
    }
    finally { $process.Dispose() }
}
$before = Inventory $project
$rows = [Collections.Generic.List[object]]::new()
try {
    $topics = @(Run-Tool $builder @('list', '--project', $project) 'source-list' | ConvertFrom-Json)
    if ($topics.Count -eq 0) { throw 'Maintained help must contain at least one topic.' }
    $sourceTopics = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $searchTopics = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $authoringNamespaces = @('http://www.madcapsoftware.com/Schemas/MadCap.xsd',
        'https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd')
    foreach ($topic in $topics) {
        $relative = $topic.Path.Replace('\', '/').ToLowerInvariant()
        if (-not $sourceTopics.Add($relative)) { throw ('Duplicate output topic path: ' + $relative) }
        $xml = Read-Xml (Join-Path (Join-Path $project 'Content') $topic.Path)
        $excluded = @($xml.Root.Attributes() | Where-Object {
            $_.Name.LocalName -ceq 'searchable' -and $authoringNamespaces -contains $_.Name.NamespaceName -and
            $_.Value.Trim().ToLowerInvariant() -in @('false', '0')
        }).Count -gt 0
        if (-not $excluded) { $searchTopics.Add($relative) | Out-Null }
    }
    $sourceAliasFile = Join-Path $project 'Project\Advanced\STATSDIRECT.flali'
    $sourceAliases = if (Test-Path -LiteralPath $sourceAliasFile) { @((Read-Xml $sourceAliasFile).Descendants('Map')) } else { @() }
    $headerFile = Join-Path $project 'Project\Advanced\SD3ContextID.h'
    $header = @{}
    if (Test-Path -LiteralPath $headerFile) {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($headerFile), '(?m)^\s*#define\s+(\w+)\s+(\d+)\b')) {
            $header[$match.Groups[1].Value] = $match.Groups[2].Value
        }
    }
    foreach ($target in @(@('DesktopHelp', 'Desktop'), @('Website', 'Public'))) {
        $directory = Join-Path $output $target[1]
        $built = Run-Tool $builder @('build', '--project', $project, '--output', $directory, '--assets', $assets, '--target', $target[0]) ('build-' + $target[1]) | ConvertFrom-Json
        $metadata = Get-Content -LiteralPath (Join-Path $directory 'build-info.json') -Raw | ConvertFrom-Json
        if ($built.TopicCount -ne $sourceTopics.Count -or $metadata.TopicCount -ne $sourceTopics.Count -or
            $built.SearchDocumentCount -ne $searchTopics.Count -or $metadata.SearchDocumentCount -ne $searchTopics.Count -or
            @($built.Warnings).Count -ne 0 -or @($metadata.Warnings).Count -ne 0) {
            throw ('Build counts or warnings do not match current source: ' + $target[0])
        }
        $index = ([IO.File]::ReadAllText((Join-Path $directory 'search-index.js')) -replace '^window\.StatsDirectSearch\s*=\s*', '' -replace ';\s*$', '') | ConvertFrom-Json
        $indexed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($document in $index.documents) {
            if (-not $searchTopics.Contains($document.url) -or -not $indexed.Add($document.url) -or
                -not (Test-Path -LiteralPath (Join-Path $directory $document.url) -PathType Leaf)) { throw ('Invalid or duplicate search target: ' + $document.url) }
        }
        if (-not $indexed.SetEquals($searchTopics)) { throw ('The search index omits current searchable topics: ' + $target[0]) }
        $generated = @((Read-Xml (Join-Path $directory 'Data\Alias.xml')).Descendants('Map'))
        if ($generated.Count -ne $sourceAliases.Count) { throw 'Generated context mapping count differs from current source.' }
        foreach ($alias in $sourceAliases) {
            $name = [string]$alias.Attribute('Name').Value
            $matches = @($generated | Where-Object { [string]$_.Attribute('Name').Value -ceq $name })
            $link = ([string]$alias.Attribute('Link').Value).Replace('\', '/').TrimStart('/') -replace '^(?i)Content/', ''
            $cut = $link.IndexOfAny([char[]]@('#', '?'))
            $expectedLink = if ($cut -lt 0) { $link.ToLowerInvariant() } else { $link.Substring(0, $cut).ToLowerInvariant() + $link.Substring($cut) }
            if ($matches.Count -ne 1 -or [string]$matches[0].Attribute('ResolvedId').Value -cne $header[$name] -or
                ([string]$matches[0].Attribute('Link').Value).TrimStart('/') -cne $expectedLink) {
                throw ('Context name/ID/destination differs from current source: ' + $name)
            }
        }
        $rows.Add([pscustomobject]@{ Target = $target[0]; TopicCount = $sourceTopics.Count; SearchDocumentCount = $indexed.Count; ContextCount = $generated.Count; Warnings = @() })
    }
    # This existing check walks all generated pages, Contents, contexts and CSS,
    # enforcing local target/anchor existence and exact case without a fixed count.
    $node = (Get-Command node -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Run-Tool $node @((Join-Path $PSScriptRoot 'output-link-checks.cjs')) 'all-local-links-assets' @{ STATSDIRECT_HELP_BUILD_ROOT = $output } | Out-Null
    $after = Inventory $project
    if ($after.Count -ne $before.Count -or @($before.Keys | Where-Object { -not $after.ContainsKey($_) -or $after[$_] -ne $before[$_] }).Count -gt 0) {
        throw 'Maintained source changed during validation.'
    }
    $result = [pscustomobject]@{ Passed = $true; CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); ProjectPath = $project;
        OutputRoot = $output; SourceTopics = $sourceTopics.Count; SourceSearchableTopics = $searchTopics.Count;
        SourceContextMaps = $sourceAliases.Count; SourceFilesUnchanged = $true; FixedCorpusAssertions = $false; Targets = @($rows) }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'maintained-help-validation.json') -Encoding utf8
    $result
}
catch {
    [ordered]@{ Passed = $false; CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); ProjectPath = $project; Error = $_.Exception.Message; Targets = @($rows) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'maintained-help-validation.json') -Encoding utf8
    throw
}
