[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$Bundle,
    [string]$WixToolPath,
    [string]$ExpectedVersion = '0.1.1',
    [string]$ExpectedCommit,
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory,
    [switch]$RequireSigned
)
$ErrorActionPreference = 'Stop'
$Msi = [IO.Path]::GetFullPath($Msi)
if (-not $Bundle) { $Bundle = Join-Path (Split-Path -Parent $Msi) 'StatsDirectHelpEditorSetup.exe' }
$Bundle = [IO.Path]::GetFullPath($Bundle)
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
function NativeIconMatches([string]$Executable, [string]$IconFile) {
    # Inspect the executable's actual Windows icon, rather than its project XML.
    # Render both icons at the same native size so PNG/DIB encoding is irrelevant.
    Add-Type -AssemblyName System.Drawing
    $actual = $null; $expected = $null; $actualBitmap = $null; $expectedBitmap = $null
    try {
        $actual = [Drawing.Icon]::ExtractAssociatedIcon($Executable)
        if (-not $actual) { return $false }
        $expected = [Drawing.Icon]::new($IconFile, $actual.Size)
        $actualBitmap = $actual.ToBitmap(); $expectedBitmap = $expected.ToBitmap()
        if ($actualBitmap.Size -ne $expectedBitmap.Size) { return $false }
        for ($y = 0; $y -lt $actualBitmap.Height; $y++) {
            for ($x = 0; $x -lt $actualBitmap.Width; $x++) {
                if ($actualBitmap.GetPixel($x, $y).ToArgb() -ne $expectedBitmap.GetPixel($x, $y).ToArgb()) { return $false }
            }
        }
        return $true
    } finally {
        foreach ($image in @($actualBitmap, $expectedBitmap, $actual, $expected)) { if ($image) { $image.Dispose() } }
    }
}
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
    $shortcuts = @(Rows 'Shortcut' @('Shortcut', 'Directory_', 'Target', 'Component_', 'Icon_', 'WkDir'))
    Check 'Start-menu and desktop shortcuts use the Help Editor target and variant icon' ($shortcuts.Count -eq 2 -and (@($shortcuts.Directory_ | Sort-Object) -join '|') -eq 'DesktopFolder|HelpEditorMenuFolder' -and @($shortcuts | Where-Object { $_.Target -ne '[INSTALLFOLDER]StatsDirectHelpEditor.exe' -or $_.WkDir -ne 'INSTALLFOLDER' -or $_.Icon_ -ne 'HelpEditorIcon' }).Count -eq 0)
    $iconPath = Join-Path $PayloadDirectory 'Assets\HelpEditor.ico'
    $iconView = Call $database 'OpenView' 'InvokeMethod' @('SELECT `Data` FROM `Icon` WHERE `Name` = ''HelpEditorIcon''')
    $iconRecord = $null
    try {
        Call $iconView 'Execute' 'InvokeMethod' | Out-Null
        $iconRecord = Call $iconView 'Fetch' 'InvokeMethod'
        $iconMatches = $false
        if ($iconRecord -and (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
            $iconLength = [int](Call $iconRecord 'DataSize' 'GetProperty' @(1))
            # msiReadStreamBytes (1) returns one byte per BSTR character.
            $binary = [string](Call $iconRecord 'ReadStream' 'InvokeMethod' @(1, $iconLength, 1))
            $expectedBytes = [IO.File]::ReadAllBytes($iconPath)
            $iconMatches = $binary.Length -eq $expectedBytes.Length
            for ($index = 0; $iconMatches -and $index -lt $binary.Length; $index++) { $iconMatches = [int][char]$binary[$index] -eq $expectedBytes[$index] }
        }
        Check 'The compiled MSI ARP and shortcut icon contains the exact packaged variant artwork' ($properties.ARPPRODUCTICON -eq 'HelpEditorIcon' -and $iconMatches)
    } finally {
        if ($iconRecord) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($iconRecord) }
        Call $iconView 'Close' 'InvokeMethod' | Out-Null
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($iconView)
    }
    Check 'The editor executable has the native Help Editor variant icon' (NativeIconMatches (Join-Path $PayloadDirectory 'StatsDirectHelpEditor.exe') $iconPath)
    Check 'The setup executable has the native Help Editor variant icon' (NativeIconMatches $Bundle $iconPath)
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
    if (-not $WixToolPath) {
        $packageFolders = @()
        if ($env:NUGET_PACKAGES) { $packageFolders += $env:NUGET_PACKAGES }
        $assetsPath = Join-Path $Root 'src\StatsDirectHelpEditor.App\obj\project.assets.json'
        if (Test-Path -LiteralPath $assetsPath) { $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json; $packageFolders += @($assets.packageFolders.PSObject.Properties.Name) }
        $packageFolders += Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
        foreach ($folder in $packageFolders) { $candidate = Join-Path $folder 'wixtoolset.sdk\7.0.1\tools\net8.0\wix.dll'; if (Test-Path -LiteralPath $candidate -PathType Leaf) { $WixToolPath = $candidate; break } }
    }
    if (-not $WixToolPath -or -not (Test-Path -LiteralPath $WixToolPath -PathType Leaf)) { throw 'The pinned WiX 7.0.1 tool must be restored, or supplied with -WixToolPath, to audit the actual bundle.' }
    $bundleRoot = Join-Path $testRoot 'compiled-bundle'
    $bundleBa = Join-Path $bundleRoot 'ba'
    $bundleContents = Join-Path $bundleRoot 'contents'
    $bundleArguments = @('burn', 'extract', '-acceptEula', 'wix7', $Bundle, '-o', $bundleContents, '-oba', $bundleBa)
    if ([IO.Path]::GetExtension($WixToolPath) -eq '.dll') { & dotnet $WixToolPath @bundleArguments *> (Join-Path $testRoot 'bundle-extract.log') }
    else { & $WixToolPath @bundleArguments *> (Join-Path $testRoot 'bundle-extract.log') }
    $bundleExtractCode = $LASTEXITCODE
    Check 'The actual setup EXE extracts successfully for compiled-engine inspection' ($bundleExtractCode -eq 0)
    if ($bundleExtractCode -ne 0) { throw 'The actual bundle could not be inspected; see bundle-extract.log.' }
    $manifest = [xml](Get-Content -LiteralPath (Join-Path $bundleBa 'manifest.xml') -Raw)
    $compiledSearches = @($manifest.SelectNodes('//*[local-name()="RegistrySearch"]'))
    $runtimeSearches = @($compiledSearches | Where-Object { $_.Id -in @('WebView2UserSearch', 'WebView2MachineSearch') })
    # Unreferenced Fragment definitions can disappear at link time. Inspecting
    # source XML alone previously missed an EXE with neither runtime search.
    Check 'Compiled Burn engine includes both WebView2 registry searches' ($runtimeSearches.Count -eq 2 -and @($runtimeSearches.Id | Sort-Object -Unique).Count -eq 2) @($runtimeSearches | ForEach-Object { $_.OuterXml })
    $searchesCorrect = $runtimeSearches.Count -eq 2
    foreach ($spec in @(@{id='WebView2UserSearch';root='HKCU';variable='WEBVIEW2USERVERSION'}, @{id='WebView2MachineSearch';root='HKLM';variable='WEBVIEW2MACHINEVERSION'})) {
        $compiled = @($runtimeSearches | Where-Object Id -eq $spec.id)
        if ($compiled.Count -ne 1 -or $compiled[0].Root -ne $spec.root -or $compiled[0].Variable -ne $spec.variable -or $compiled[0].Key -ne 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -or $compiled[0].Value -ne 'pv' -or $compiled[0].Type -ne 'value' -or $compiled[0].Win64 -eq 'yes') { $searchesCorrect = $false }
    }
    Check 'Compiled searches assign the required variables from HKCU/HKLM 32-bit pv values' $searchesCorrect
    $compiledRuntime = $manifest.SelectSingleNode('//*[local-name()="ExePackage" and @Id="WebView2Runtime"]')
    $compiledMsi = $manifest.SelectSingleNode('//*[local-name()="MsiPackage" and @Id="HelpEditorMsi"]')
    $registration = $manifest.SelectSingleNode('//*[local-name()="Registration"]')
    Check 'Compiled chain remains per-user and retains the shared WebView2 runtime' ($registration.Scope -eq 'perUser' -and $compiledRuntime.Scope -eq 'perUser' -and $compiledMsi.Scope -eq 'perUser' -and $compiledRuntime.Permanent -eq 'yes' -and $compiledRuntime.InstallArguments -eq '/silent /install' -and $compiledRuntime.DetectCondition -eq 'WEBVIEW2USERVERSION > v0.0.0.0 OR WEBVIEW2MACHINEVERSION > v0.0.0.0')
    $msiPayload = $manifest.SelectSingleNode('//*[local-name()="Payload" and @Id="HelpEditorMsi"]')
    $embeddedMsi = Join-Path (Join-Path $bundleContents $msiPayload.Container) $msiPayload.FilePath
    Check 'The setup EXE embeds the exact MSI being audited' ((Test-Path -LiteralPath $embeddedMsi -PathType Leaf) -and (Hash $embeddedMsi) -eq (Hash $Msi))
    $runtimePayload = $manifest.SelectSingleNode('//*[local-name()="Payload" and @Id="WebView2Runtime"]')
    $embeddedRuntime = Join-Path (Join-Path $bundleContents $runtimePayload.Container) $runtimePayload.FilePath
    $embeddedRuntimeSignature = if (Test-Path -LiteralPath $embeddedRuntime -PathType Leaf) { Get-AuthenticodeSignature -LiteralPath $embeddedRuntime } else { $null }
    Check 'The setup EXE embeds a trusted Microsoft WebView2 bootstrapper' ($embeddedRuntimeSignature.Status -eq 'Valid' -and $embeddedRuntimeSignature.SignerCertificate.Subject -match '(^|,\s*)O=Microsoft Corporation(,|$)')
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
        foreach ($file in @($Msi, $Bundle, (Join-Path $PayloadDirectory 'StatsDirectHelpEditor.exe'), (Join-Path $PayloadDirectory 'Builder\StatsDirectHelpBuilder.exe'))) { $signature = Get-AuthenticodeSignature -LiteralPath $file; Check ('Valid timestamped signature: ' + (Split-Path -Leaf $file)) ($signature.Status -eq 'Valid' -and [bool]$signature.TimeStamperCertificate) }
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
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'installer-package-checks.json'), ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Host ($passed.ToString() + ' installer package checks passed; ' + $failed + ' failed. Report: ' + $reportPath)
if ($failed) { throw 'Installer package checks failed. See the JSON report.' }
