[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$ExpectedVersion = '0.1.0',
    [string]$ExpectedCommit,
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory,
    [switch]$RequireSigned
)
$ErrorActionPreference = 'Stop'
$Msi = [IO.Path]::GetFullPath($Msi)
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
$Root = [IO.Path]::GetFullPath($Root)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $Root 'evidence' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$testRoot = Join-Path $OutputDirectory ('InstallerPackageCheck-' + [Guid]::NewGuid().ToString('N'))
$extract = Join-Path $testRoot 'extracted'
New-Item -ItemType Directory -Path $extract -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed, [object]$Detail = $null) {
    $checks.Add([pscustomobject]@{name=$Name;passed=$Passed;detail=$Detail})
    Write-Host (('{0} {1}' -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name))
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Call($Object, [string]$Name, [string]$Kind, [object[]]$Arguments = $null) { $Object.GetType().InvokeMember($Name, $Kind, $null, $Object, $Arguments) }
$engine = $null
$database = $null
try {
    $engine = New-Object -ComObject WindowsInstaller.Installer
    $database = Call $engine 'OpenDatabase' 'InvokeMethod' @($Msi, 0)
    $knownTables = @{}
    function Rows([string]$Table, [string[]]$Columns) {
        if ($Table -ne '_Tables' -and -not $knownTables.ContainsKey($Table)) { return }
        $sql = 'SELECT ' + (($Columns | ForEach-Object { '`' + $_ + '`' }) -join ', ') + ' FROM `' + $Table + '`'
        $view = Call $database 'OpenView' 'InvokeMethod' @($sql)
        try {
            Call $view 'Execute' 'InvokeMethod' | Out-Null
            while ($null -ne ($record = Call $view 'Fetch' 'InvokeMethod')) {
                try {
                    $values = [ordered]@{}
                    for ($index = 0; $index -lt $Columns.Count; $index++) { $values[$Columns[$index]] = Call $record 'StringData' 'GetProperty' @(($index + 1)) }
                    [pscustomobject]$values
                } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record) }
            }
        } finally { Call $view 'Close' 'InvokeMethod' | Out-Null; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view) }
    }
    foreach ($row in Rows '_Tables' @('Name')) { $knownTables[$row.Name] = $true }
    $properties = @{}
    foreach ($row in Rows 'Property' @('Property', 'Value')) { $properties[$row.Property] = $row.Value }
    Check 'The actual MSI has the expected product version and stable upgrade identity' ($properties.ProductVersion -eq $ExpectedVersion -and $properties.UpgradeCode -eq '{272143C2-41D6-4DBD-A4C0-3AB22F09CD24}')
    Check 'The actual MSI is per-user, without ALLUSERS or an administrator launch requirement' (-not $properties.ContainsKey('ALLUSERS') -and -not $properties.ContainsKey('MSIUSEREALADMINDETECTION'))
    $summary = Call $database 'SummaryInformation' 'GetProperty' @(0)
    try {
        $wordCount = [int](Call $summary 'Property' 'GetProperty' @(15))
        $template = [string](Call $summary 'Property' 'GetProperty' @(7))
        Check 'MSI summary metadata declares a limited-user x64 package' (($wordCount -band 8) -ne 0 -and $template -match '^x64;') ([pscustomobject]@{wordCount=$wordCount;template=$template})
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($summary) }
    $registry = @{}
    foreach ($row in Rows 'Registry' @('Registry', 'Root', 'Key', 'Name', 'Value', 'Component_')) { $registry[$row.Registry] = $row }
    $components = @{}
    foreach ($row in Rows 'Component' @('Component', 'Directory_', 'Attributes', 'KeyPath')) { $components[$row.Component] = $row }
    $directories = @{}
    foreach ($row in Rows 'Directory' @('Directory', 'Directory_Parent', 'DefaultDir')) { $directories[$row.Directory] = $row }
    Check 'Installation uses the user LocalAppData Programs folder' ($directories.INSTALLFOLDER.Directory_Parent -eq 'UserProgramsFolder' -and $directories.UserProgramsFolder.Directory_Parent -eq 'LocalAppDataFolder')
    Check 'Every component has an HKCU registry key path' (@($components.Values | Where-Object { ([int]$_.Attributes -band 4) -eq 0 -or -not $registry.ContainsKey($_.KeyPath) -or [int]$registry[$_.KeyPath].Root -ne 1 }).Count -eq 0) $components.Count
    Check 'Installer registry writes remain in its own per-user key' (@($registry.Values | Where-Object { [int]$_.Root -ne 1 -or -not $_.Key.StartsWith('Software\StatsDirect\HelpEditor\Installer') }).Count -eq 0)
    $locators = @(Rows 'RegLocator' @('Signature_', 'Root', 'Key', 'Name', 'Type'))
    $runtimeLocators = @($locators | Where-Object { $_.Key -like '*{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}*' })
    Check 'Direct MSI detects both per-user and machine WebView2 in the 32-bit registry' ($runtimeLocators.Count -eq 2 -and @($runtimeLocators | Where-Object { ([int]$_.Type -band 16) -ne 0 -or $_.Name -ne 'pv' }).Count -eq 0 -and @($runtimeLocators.Root | Sort-Object) -join ',' -eq '1,2')
    $launch = @(Rows 'LaunchCondition' @('Condition', 'Description'))
    Check 'The direct MSI reports a missing WebView2 runtime and permits maintenance' (@($launch | Where-Object { $_.Condition -like 'Installed OR*WEBVIEW2USERVERSION*WEBVIEW2MACHINEVERSION*' -and $_.Description -like '*StatsDirectHelpEditorSetup.exe*' }).Count -eq 1)
    $shortcuts = @(Rows 'Shortcut' @('Shortcut', 'Directory_', 'Target', 'Component_'))
    Check 'Start-menu shortcut points to the editor and no desktop shortcut is added' ($shortcuts.Count -eq 1 -and $shortcuts[0].Directory_ -eq 'HelpEditorMenuFolder' -and $shortcuts[0].Target -eq '[INSTALLFOLDER]StatsDirectHelpEditor.exe')
    $customActions = @(Rows 'CustomAction' @('Action', 'Type', 'Source', 'Target'))
    Check 'The MSI has no application launch, workspace deletion or elevation custom actions' (@($customActions | Where-Object { $_.Source -ne 'WixUIWixca_X64' -and $_.Action -notlike 'WixUI*' }).Count -eq 0) @($customActions.Action)
    $removal = @(Rows 'RemoveFile' @('FileKey', 'Component_', 'FileName', 'DirProperty', 'InstallMode'))
    Check 'Removal only attempts empty installation/menu folders, never user workspace files' ($removal.Count -gt 0 -and @($removal | Where-Object { $_.FileName -or [int]$_.InstallMode -ne 2 -or $_.DirProperty -like '*Workspace*' }).Count -eq 0)
    function RelativeDirectory([string]$Id) {
        if ($Id -eq 'INSTALLFOLDER') { return '' }
        if (-not $directories.ContainsKey($Id) -or -not $directories[$Id].Directory_Parent) { throw ('Payload file is outside INSTALLFOLDER: ' + $Id) }
        $parent = RelativeDirectory $directories[$Id].Directory_Parent
        $leaf = $directories[$Id].DefaultDir.Split(':')[0].Split('|')[-1]
        if ($parent) { return (Join-Path $parent $leaf) }
        return $leaf
    }
    $files = @(Rows 'File' @('File', 'Component_', 'FileName', 'FileSize'))
    $packaged = @{}
    foreach ($file in $files) { $folder = RelativeDirectory $components[$file.Component_].Directory_; $leaf = $file.FileName.Split('|')[-1]; $relative = if ($folder) { Join-Path $folder $leaf } else { $leaf }; $packaged[$relative] = $file }
    $payloadFiles = @(Get-ChildItem -LiteralPath $PayloadDirectory -File -Recurse)
    Check 'MSI file inventory matches every staged payload file' ($files.Count -eq $payloadFiles.Count -and @($payloadFiles | Where-Object { -not $packaged.ContainsKey($_.FullName.Substring($PayloadDirectory.Length + 1)) }).Count -eq 0) ([pscustomobject]@{msi=$files.Count;payload=$payloadFiles.Count})
    Check 'Packaged source excludes Flare user data, publish destinations and private/generated state' (@($packaged.Keys | Where-Object { $_ -match '(^|\\)(\.git|\.help-editor-recovery|\.editor-profile|\.app-help-preview-profile|WorkingCopies|evidence|bin|obj)(\\|$)' -or $_ -match '^HelpSource\\Project\\(Users|Destinations)(\\|$)' -or $_ -match '(^|\\)(auth\.json|credentials\.json)$' }).Count -eq 0)
    $requiredNotices = @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'AppHelp\LICENSE.StatsDirect.txt', 'Web\mathlive\LICENSE.txt', 'Web\mathlive\fonts\LICENSE.txt', 'Web\mathjax\LICENSE', 'Web\OpenAI\LICENSE', 'Web\OpenAI\NOTICE', 'ThirdParty\NETCore-runtime\LICENSE.TXT', 'ThirdParty\NETCore-runtime\THIRD-PARTY-NOTICES.TXT', 'ThirdParty\WindowsDesktop-runtime\LICENSE', 'ThirdParty\WebView2-SDK\LICENSE.txt', 'ThirdParty\WebView2-SDK\NOTICE.txt', 'HelpSource\LICENSE')
    Check 'All application, content and redistributable notices are included' (@($requiredNotices | Where-Object { -not $packaged.ContainsKey($_) }).Count -eq 0)
    $infoText = Get-Content -LiteralPath (Join-Path $PayloadDirectory 'build-info.json') -Raw
    $info = $infoText | ConvertFrom-Json
    Check 'Installed build metadata has expected version, runtime versions and no absolute machine paths' ($info.version -eq $ExpectedVersion -and $info.coreRuntimeVersion -and $info.desktopRuntimeVersion -and $info.webView2SdkVersion -and $info.sourceInventorySha256 -match '^[a-f0-9]{64}$' -and $infoText -notmatch '\b[A-Za-z]:[\\/]')
    if ($ExpectedCommit) { Check 'Installed build metadata identifies the exact release commit' ($info.sourceCommit -eq $ExpectedCommit) }
    $bundleSource = [xml](Get-Content -LiteralPath (Join-Path $Root 'installer\Bundle.wxs') -Raw)
    $runtime = $bundleSource.SelectSingleNode('//*[local-name()="ExePackage"]')
    $searches = @($bundleSource.SelectNodes('//*[local-name()="RegistrySearch"]'))
    Check 'Bundle prerequisite is per-user, permanent and skipped when either registry location has WebView2' ($runtime.PerMachine -eq 'no' -and $runtime.Permanent -eq 'yes' -and $runtime.InstallArguments -eq '/silent /install' -and $runtime.DetectCondition -like '*WEBVIEW2USERVERSION*OR*WEBVIEW2MACHINEVERSION*' -and $searches.Count -eq 2 -and @($searches | Where-Object Bitness -ne 'always32').Count -eq 0)
    $beforeHash = Hash $Msi
    $extractLog = Join-Path $testRoot 'administrative-extract.log'
    $process = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList @('/a', ('"' + $Msi + '"'), '/qn', ('TARGETDIR="' + $extract + '"'), '/l*v', ('"' + $extractLog + '"')) -WindowStyle Hidden -PassThru -Wait
    Check 'Administrative extraction succeeds without installing the product or modifying its MSI' ($process.ExitCode -eq 0 -and (Hash $Msi) -eq $beforeHash) $process.ExitCode
    $executables = @(Get-ChildItem -LiteralPath $extract -Recurse -File -Filter StatsDirectHelpEditor.exe)
    Check 'The administrative image contains one editor executable' ($executables.Count -eq 1)
    if ($executables.Count -eq 1) {
        $extractedRoot = Split-Path -Parent $executables[0].FullName
        $differences = @()
        foreach ($file in $payloadFiles) { $relative = $file.FullName.Substring($PayloadDirectory.Length + 1); $copy = Join-Path $extractedRoot $relative; if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or (Hash $copy) -ne (Hash $file.FullName)) { $differences += $relative } }
        Check 'Every administratively extracted file matches the staged payload SHA256' ($differences.Count -eq 0) $differences
    }
    if ($RequireSigned) {
        foreach ($file in @($Msi, (Join-Path $PayloadDirectory 'StatsDirectHelpEditor.exe'), (Join-Path $PayloadDirectory 'Builder\StatsDirectHelpBuilder.exe'))) { $signature = Get-AuthenticodeSignature -LiteralPath $file; Check ('Valid timestamped signature: ' + (Split-Path -Leaf $file)) ($signature.Status -eq 'Valid' -and [bool]$signature.TimeStamperCertificate) }
    }
} catch { Check 'Installer package audit completed without unexpected failures' $false $_.Exception.ToString() }
finally {
    if ($database) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($database) }
    if ($engine) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($engine) }
}
$passed = @($checks | Where-Object passed).Count
$failed = @($checks | Where-Object { -not $_.passed }).Count
$report = [ordered]@{checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');passed=$passed;failed=$failed;administrativeExtractionOnly=$true;checks=@($checks)}
$reportPath = Join-Path $testRoot 'installer-package-checks.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Host ($passed.ToString() + ' installer package checks passed; ' + $failed + ' failed. Report: ' + $reportPath)
if ($failed) { throw 'Installer package checks failed. See the JSON report.' }
