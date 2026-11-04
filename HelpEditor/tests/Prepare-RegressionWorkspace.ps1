#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EditorRoot,
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($EditorRoot).TrimEnd('\', '/')
$destination = [IO.Path]::GetFullPath($DestinationDirectory).TrimEnd('\', '/')
if (Test-Path -LiteralPath $destination) { throw 'Use a fresh, short regression workspace; existing files are never replaced.' }
if ($destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The regression workspace must be outside the maintained editor checkout.'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RegressionSourceFixture.json') -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.commit -ne '96c2bb257064a1113514a781f796ac3ecb8caf8b' -or
    $manifest.treePath -ne 'HelpEditor/HelpSource' -or $manifest.inventorySha256 -ne '2aab3fec8daddf492722b1c093560cb4832dcfd394e38a90349ab9f92a7bef59') {
    throw 'The historical source fixture identity is invalid. Do not refresh it for author edits.'
}
$git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
function Invoke-Git([string[]]$Arguments, [switch]$AllowFailure) {
    $start = [Diagnostics.ProcessStartInfo]::new($git)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.Environment['GIT_TERMINAL_PROMPT'] = '0'
    foreach ($arg in @('-c', 'core.longpaths=true', '-c', 'credential.interactive=never') + $Arguments) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Git fixture preparation timed out.' }
        $text = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -and -not $AllowFailure) { throw ('Git fixture preparation failed: ' + $errorText) }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $text.Trim() }
    }
    finally { $process.Dispose() }
}
if (-not $RepositoryRoot) { $RepositoryRoot = (Invoke-Git @('-C', $source, 'rev-parse', '--show-toplevel')).Output }
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
if ((Invoke-Git @('-C', $repository, 'cat-file', '-e', ($manifest.commit + '^{commit}')) -AllowFailure).ExitCode -ne 0) {
    Invoke-Git @('-C', $repository, 'fetch', '--no-tags', '--depth=1', $manifest.repository, $manifest.commit) | Out-Null
}
Invoke-Git @('-C', $repository, 'cat-file', '-e', ($manifest.commit + '^{commit}')) | Out-Null

New-Item -ItemType Directory -Path $destination | Out-Null
$copied = [Collections.Generic.List[object]]::new()
$allowedDirectories = @('src', 'tests', 'Schema', 'TutorSource', 'TutorExamples', 'docs', 'installer', '.github')
$excludedDirectories = @('bin', 'obj', 'node_modules', 'App', 'Build', 'evidence', 'Release', 'ReleaseWork', 'Settings',
    '.git', '.vs', '.help-editor-recovery', '__pycache__', 'TestResults', 'Publishing', 'WorkingCopies')
function Copy-SourceTree([string]$Directory, [string]$Relative) {
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        $relativePath = if ($Relative) { $Relative + '/' + $entry.Name } else { $entry.Name }
        if ($entry.PSIsContainer) {
            if ($excludedDirectories -contains $entry.Name -or $entry.Name -match '(?i)(?:^|[-.])profile$') { continue }
            # Matches this repository's tests/*/*/ ignore rule: nested project
            # directories are generated fixtures/profiles, not test source.
            if ($relativePath.StartsWith('tests/') -and $relativePath.Split('/').Count -ge 3) { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Linked source directory: ' + $relativePath) }
            Copy-SourceTree $entry.FullName $relativePath
        }
        else {
            if ($entry.Name -like '*.user') { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Linked source file: ' + $relativePath) }
            $bytes = [IO.File]::ReadAllBytes($entry.FullName)
            $target = Join-Path $destination $relativePath
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.File]::WriteAllBytes($target, $bytes)
            $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            $copied.Add([pscustomobject]@{ path = $relativePath; source = $entry.FullName; sha256 = $sha })
        }
    }
}
foreach ($directory in $allowedDirectories) {
    $path = Join-Path $source $directory
    if (Test-Path -LiteralPath $path) { Copy-SourceTree $path $directory }
}
foreach ($file in Get-ChildItem -LiteralPath $source -File -Force) {
    if ($file.Name -in @('LICENSE', '.gitignore', '.gitattributes') -or $file.Extension -in @('.slnx', '.ps1', '.cmd', '.props', '.targets', '.json', '.md', '.txt')) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Linked source file: ' + $file.Name) }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name)
        $copied.Add([pscustomobject]@{ path = $file.Name; source = $file.FullName; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
}
foreach ($file in $copied) {
    if ((Get-FileHash -LiteralPath $file.source -Algorithm SHA256).Hash -ne $file.sha256 -or
        (Get-FileHash -LiteralPath (Join-Path $destination $file.path) -Algorithm SHA256).Hash -ne $file.sha256) {
        throw ('Implementation changed during source mirroring: ' + $file.path)
    }
}

$archive = Join-Path $destination '.historical-help-source.tar'
Invoke-Git @('-C', $repository, 'archive', '--format=tar', ('--output=' + $archive), $manifest.commit, $manifest.treePath) | Out-Null
$fixture = Join-Path $destination 'HelpSource'
New-Item -ItemType Directory -Path $fixture | Out-Null
$tar = if ($IsWindows) { Join-Path $env:SystemRoot 'System32\tar.exe' } else { (Get-Command tar -CommandType Application).Source }
& $tar -xf $archive -C $fixture --strip-components 2
if ($LASTEXITCODE -ne 0) { throw 'The pinned source archive could not be extracted.' }
$actual = @(Get-ChildItem -LiteralPath $fixture -Recurse -File -Force)
if ($actual.Count -ne $manifest.fileCount) { throw 'The extracted historical source has an unexpected file count.' }
$canonical = [Text.StringBuilder]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.files) {
    if ($file.path -match '(^/|\\|(^|/)\.\.?(/|$)|:)' -or -not $seen.Add($file.path)) { throw 'Unsafe historical source inventory path.' }
    $path = Join-Path $fixture $file.path
    $info = Get-Item -LiteralPath $path
    if ($info.PSIsContainer -or $info.Attributes -band [IO.FileAttributes]::ReparsePoint -or $info.Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw ('Historical source inventory mismatch: ' + $file.path) }
    $canonical.Append($file.path).Append([char]0).Append(([long]$file.bytes).ToString([Globalization.CultureInfo]::InvariantCulture)).Append([char]0).Append($file.sha256).Append("`n") | Out-Null
}
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical.ToString()))).ToLowerInvariant()
if ($digest -ne $manifest.inventorySha256) { throw 'Historical source inventory digest differs.' }
$result = [pscustomobject]@{
    WorkspaceRoot = $destination; FixtureRoot = $fixture; FixtureCommit = $manifest.commit
    FixtureInventorySha256 = $digest; FixtureFiles = $actual.Count; MirroredImplementationFiles = $copied.Count
    BrowserDependencies = Join-Path $source 'tests\browser\node_modules'
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'regression-workspace.json') -Encoding utf8
$result
