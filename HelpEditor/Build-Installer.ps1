[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.1',
    [string]$SourceProject = (Join-Path $PSScriptRoot 'HelpSource'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Release'),
    [switch]$Sign,
    [string]$SignToolPath,
    [string]$CertificateThumbprint = $env:STATSDIRECT_SIGNING_THUMBPRINT,
    [string]$TimestampUrl = 'http://timestamp.globalsign.com/tsa/r6advanced1'
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'WiX installer builds require Windows.' }
$parsedVersion = [version]$Version
if ($parsedVersion.Major -gt 255 -or $parsedVersion.Minor -gt 255 -or $parsedVersion.Build -gt 65535) { throw 'Version exceeds Windows Installer version limits.' }
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$SourceProject = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SourceProject)
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
foreach ($protected in @($SourceProject, (Join-Path $root 'App'), (Join-Path $root 'src'), (Join-Path $root 'tests'))) {
    if ($OutputDirectory.TrimEnd('\') -eq $protected.TrimEnd('\') -or $OutputDirectory.StartsWith($protected.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer output must be separate from source, the working HelpSource and the runnable App.' }
}
foreach ($required in @('Content', 'Project', 'StatsDirect.flprj', 'LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceProject $required))) { throw ('Help source is incomplete: ' + $required) }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 10 SDK is required to build the installer.' }
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw ($Command + ' failed with exit code ' + $LASTEXITCODE) }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function TextHash([string]$Text) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
function Write-Json([object]$Value, [string]$Path) {
    $text = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
$gitCommit = $null
$sourceDate = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = & git -C $root rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $gitCommit = ($candidate -join '').Trim()
        $sourceDate = ((& git -C $root show -s --format=%cI HEAD) -join '').Trim()
        $changes = @(& git -C $root status --porcelain --untracked-files=normal)
        if ($Sign -and $changes.Count) { throw 'Commit the reviewed source before building a signed release; the Git checkout has changes.' }
    }
}
if ($Sign) {
    if (-not $gitCommit) { throw 'A signed release must be built from committed Git source.' }
    if (-not $SourceProject.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A signed release must use help source inside the committed project checkout.' }
    if (-not $SignToolPath) { $found = Get-Command signtool.exe -ErrorAction SilentlyContinue; if ($found) { $SignToolPath = $found.Source } }
    if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'Pass -SignToolPath pointing to the Windows SDK SignTool executable.' }
    $SignToolPath = [IO.Path]::GetFullPath($SignToolPath)
    $CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($CertificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Pass the code-signing certificate SHA1 thumbprint with -CertificateThumbprint.' }
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $CertificateThumbprint) -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'The selected current-user signing certificate is unavailable or expired.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$work = Join-Path $OutputDirectory ('.work\' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$products = Join-Path $work 'products'
New-Item -ItemType Directory -Path $payload, $products -Force | Out-Null
Invoke-Checked dotnet @('publish', (Join-Path $root 'src\StatsDirectHelpEditor.App\StatsDirectHelpEditor.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', ('-p:Version=' + $Version), '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $payload, '--nologo', '--verbosity', 'minimal')
Invoke-Checked dotnet @('publish', (Join-Path $root 'src\StatsDirectHelpEditor.Cli\StatsDirectHelpEditor.Cli.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', ('-p:Version=' + $Version), '-p:DebugType=None', '-p:DebugSymbols=false', '-o', (Join-Path $payload 'Builder'), '--nologo', '--verbosity', 'minimal')
$seed = Join-Path $payload 'HelpSource'
New-Item -ItemType Directory -Path $seed -Force | Out-Null
foreach ($folder in @('Content', 'Project', 'RCode')) {
    $start = Join-Path $SourceProject $folder
    if (-not (Test-Path -LiteralPath $start -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $start -File -Recurse -Force) {
        $relative = $file.FullName.Substring($SourceProject.TrimEnd('\').Length + 1)
        if ($relative -match '(^|\\)(\.git|\.help-editor-recovery|\.editor-profile|\.app-help-preview-profile|bin|obj|Build|Output)(\\|$)' -or $relative -match '^Project\\(Users|Destinations)(\\|$)') { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Help source must not contain linked files.' }
        $destination = Join-Path $seed $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
foreach ($name in @('StatsDirect.flprj', 'LICENSE', 'README.md')) { $file = Join-Path $SourceProject $name; if (Test-Path -LiteralPath $file -PathType Leaf) { Copy-Item -LiteralPath $file -Destination (Join-Path $seed $name) } }
Write-Json ([ordered]@{prototypeWorkingCopy=$true; sourceRepository='https://github.com/iain-buchan/statisticalhelp'; sourceCommit=$gitCommit; sourceStatus='Packaged reviewed source; personal Flare settings and publishing destinations excluded.'}) (Join-Path $seed '.statsdirect-help-editor.json')
Copy-Item -LiteralPath (Join-Path $root 'Build-Help.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $payload
if (Test-Path -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $payload }
$resolvedAssets = Get-Content -LiteralPath (Join-Path $root 'src\StatsDirectHelpEditor.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageFolders = @($resolvedAssets.packageFolders.PSObject.Properties.Name)
function Copy-VendorNotice([string]$PackagePath, [string]$Destination) {
    $found = $null
    foreach ($packageFolder in $packageFolders) { $candidate = Join-Path $packageFolder $PackagePath; if (Test-Path -LiteralPath $candidate -PathType Leaf) { $found = $candidate; break } }
    if (-not $found) { throw ('Required redistributable notice is missing from the resolved NuGet package folders: ' + $PackagePath) }
    $destinationPath = Join-Path $payload ('ThirdParty\' + $Destination)
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $found -Destination $destinationPath
}
$runtimeConfiguration = Get-Content -LiteralPath (Join-Path $payload 'StatsDirectHelpEditor.runtimeconfig.json') -Raw | ConvertFrom-Json
$coreRuntime = @($runtimeConfiguration.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App')
$desktopRuntime = @($runtimeConfiguration.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App')
if ($coreRuntime.Count -ne 1 -or $desktopRuntime.Count -ne 1) { throw 'The published editor must include its exact self-contained .NET and Windows Desktop runtime versions.' }
$coreRuntimeVersion = [string]$coreRuntime[0].version
$desktopRuntimeVersion = [string]$desktopRuntime[0].version
Copy-VendorNotice ('microsoft.netcore.app.runtime.win-x64\' + $coreRuntimeVersion + '\LICENSE.TXT') 'NETCore-runtime\LICENSE.TXT'
Copy-VendorNotice ('microsoft.netcore.app.runtime.win-x64\' + $coreRuntimeVersion + '\THIRD-PARTY-NOTICES.TXT') 'NETCore-runtime\THIRD-PARTY-NOTICES.TXT'
Copy-VendorNotice ('microsoft.windowsdesktop.app.runtime.win-x64\' + $desktopRuntimeVersion + '\LICENSE') 'WindowsDesktop-runtime\LICENSE'
$webViewPackage = @($resolvedAssets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.Web.WebView2/*' })
if ($webViewPackage.Count -ne 1) { throw 'Could not resolve the exact Microsoft.Web.WebView2 SDK package version.' }
$webViewVersion = $webViewPackage[0].Split('/')[1]
Copy-VendorNotice ('microsoft.web.webview2\' + $webViewVersion + '\LICENSE.txt') 'WebView2-SDK\LICENSE.txt'
Copy-VendorNotice ('microsoft.web.webview2\' + $webViewVersion + '\NOTICE.txt') 'WebView2-SDK\NOTICE.txt'
Write-Json ([ordered]@{schemaVersion=1; version=$Version}) (Join-Path $payload '.statsdirect-help-editor-install.json')
$inventory = @()
foreach ($folder in @('src', 'installer')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse) {
        $relative = $file.FullName.Substring($root.TrimEnd('\').Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(bin|obj)(/|$)') { continue }
        $inventory += [pscustomobject]@{path=$relative; sha256=(Hash $file.FullName)}
    }
}
foreach ($file in Get-ChildItem -LiteralPath $root -File) {
    if ($file.Extension -in @('.ps1', '.props', '.targets', '.slnx', '.json', '.md') -or $file.Name -eq 'LICENSE') { $inventory += [pscustomobject]@{path=$file.Name; sha256=(Hash $file.FullName)} }
}
foreach ($file in Get-ChildItem -LiteralPath $seed -Recurse -File) { $inventory += [pscustomobject]@{path=('HelpSource/' + $file.FullName.Substring($seed.Length + 1).Replace('\', '/')); sha256=(Hash $file.FullName)} }
$inventory = @($inventory | Sort-Object path)
$inventoryHash = TextHash (($inventory | ForEach-Object { $_.path + ' ' + $_.sha256 }) -join "`n")
if (-not $sourceDate) { $sourceDate = (Get-Item (Join-Path $root 'Build-Installer.ps1')).LastWriteTimeUtc.ToString('o') }
Write-Json ([ordered]@{schemaVersion=1; product='StatsDirect Help Editor'; version=$Version; runtime='win-x64 self-contained .NET 10'; coreRuntimeVersion=$coreRuntimeVersion; desktopRuntimeVersion=$desktopRuntimeVersion; webView2SdkVersion=$webViewVersion; sourceRepository='https://github.com/iain-buchan/statisticalhelp'; sourceCommit=$gitCommit; sourceDateUtc=$sourceDate; sourceInventorySha256=$inventoryHash; sourceFileCount=$inventory.Count}) (Join-Path $payload 'build-info.json')
Write-Json $inventory (Join-Path $work 'source-inventory.json')
$prerequisite = Join-Path $work 'MicrosoftEdgeWebview2Setup.exe'
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $prerequisite -UseBasicParsing
$microsoftSignature = Get-AuthenticodeSignature -LiteralPath $prerequisite
if ($microsoftSignature.Status -ne 'Valid' -or $microsoftSignature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') { throw 'The WebView2 bootstrapper does not have a valid Microsoft signature.' }
$environmentNames = @('SD_HELP_SIGNTOOL_PATH', 'SD_HELP_CERT_THUMBPRINT', 'SD_HELP_TIMESTAMP_URL')
$oldEnvironment = @{}
foreach ($name in $environmentNames) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    if ($Sign) {
        $env:SD_HELP_SIGNTOOL_PATH = $SignToolPath
        $env:SD_HELP_CERT_THUMBPRINT = $CertificateThumbprint
        $env:SD_HELP_TIMESTAMP_URL = $TimestampUrl
        foreach ($relative in @('StatsDirectHelpEditor.exe', 'StatsDirectHelpEditor.dll', 'StatsDirectHelpEditor.Core.dll', 'Builder\StatsDirectHelpBuilder.exe', 'Builder\StatsDirectHelpBuilder.dll', 'Builder\StatsDirectHelpEditor.Core.dll')) { & (Join-Path $root 'installer\Sign-Artifact.ps1') -Path (Join-Path $payload $relative) }
    }
    $authoring = Join-Path $work 'Payload.wxs'
    & (Join-Path $root 'installer\New-PayloadAuthoring.ps1') -PayloadDirectory $payload -OutputFile $authoring
    $signedProperty = if ($Sign) { 'true' } else { 'false' }
    Invoke-Checked dotnet @('build', (Join-Path $root 'installer\HelpEditor.wixproj'), '-c', 'Release', '-p:AcceptEula=wix7', ('-p:BuildVersion=' + $Version), ('-p:PayloadDirectory=' + $payload), ('-p:PayloadAuthoring=' + $authoring), ('-p:SignOutput=' + $signedProperty), ('-p:OutputPath=' + $products + '\'), ('-p:BaseIntermediateOutputPath=' + (Join-Path $work 'msi-obj') + '\'), '--nologo', '--verbosity', 'minimal')
    $msi = Join-Path $products 'StatsDirectHelpEditor-x64.msi'
    Invoke-Checked dotnet @('build', (Join-Path $root 'installer\HelpEditor.Bundle.wixproj'), '-c', 'Release', '-p:AcceptEula=wix7', ('-p:BuildVersion=' + $Version), ('-p:MsiPath=' + $msi), ('-p:WebView2Bootstrapper=' + $prerequisite), ('-p:PayloadDirectory=' + $payload), ('-p:SignOutput=' + $signedProperty), ('-p:OutputPath=' + $products + '\'), ('-p:BaseIntermediateOutputPath=' + (Join-Path $work 'bundle-obj') + '\'), '--nologo', '--verbosity', 'minimal')
    $artifacts = @()
    foreach ($name in @('StatsDirectHelpEditor-x64.msi', 'StatsDirectHelpEditorSetup.exe')) {
        $file = Join-Path $products $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Installer build did not produce ' + $name) }
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($Sign -and ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or -not $signature.TimeStamperCertificate)) { throw ('Final installer signature did not verify: ' + $name) }
        $artifacts += [pscustomobject]@{file=$name; bytes=(Get-Item -LiteralPath $file).Length; sha256=(Hash $file); signatureStatus=[string]$signature.Status; signer=if($signature.SignerCertificate){$signature.SignerCertificate.Subject}else{$null}; timestamped=[bool]$signature.TimeStamperCertificate}
    }
    $receipt = [ordered]@{schemaVersion=1; product='StatsDirect Help Editor'; version=$Version; builtAtUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceRepository='https://github.com/iain-buchan/statisticalhelp'; sourceCommit=$gitCommit; sourceInventorySha256=$inventoryHash; signed=[bool]$Sign; wixVersion='7.0.1'; scope='perUser'; prerequisites=[ordered]@{WebView2BootstrapperSha256=(Hash $prerequisite); publisher=$microsoftSignature.SignerCertificate.Subject; signatureStatus=[string]$microsoftSignature.Status; internetNeededOnlyIfWebView2Missing=$true; bundledDotNetRuntime=$true}; artifacts=$artifacts; published=$false}
    foreach ($inputFile in $inventory) {
        if ($inputFile.path -eq 'HelpSource/.statsdirect-help-editor.json') { continue }
        $livePath = if ($inputFile.path.StartsWith('HelpSource/')) { Join-Path $SourceProject $inputFile.path.Substring(11) } else { Join-Path $root $inputFile.path }
        if (-not (Test-Path -LiteralPath $livePath -PathType Leaf) -or (Hash $livePath) -ne $inputFile.sha256) { throw ('Source changed during the installer build: ' + $inputFile.path + '. Candidates have not been replaced.') }
    }
    if ($Sign) {
        $finalCommit = ((& git -C $root rev-parse HEAD) -join '').Trim()
        $finalChanges = @(& git -C $root status --porcelain --untracked-files=normal)
        if ($LASTEXITCODE -ne 0 -or $finalCommit -ne $gitCommit -or $finalChanges.Count) { throw 'The source commit or working tree changed during signing. Candidates have not been replaced.' }
    }
    # Replace public candidates only after both complete products and their
    # requested signatures have passed verification. The runnable App is untouched.
    foreach ($artifact in $artifacts) { Copy-Item -LiteralPath (Join-Path $products $artifact.file) -Destination (Join-Path $OutputDirectory $artifact.file) -Force }
    Write-Json $receipt (Join-Path $OutputDirectory 'installer-release.json')
    [pscustomobject]@{OutputDirectory=$OutputDirectory; PayloadDirectory=$payload; Receipt=(Join-Path $OutputDirectory 'installer-release.json'); Result=$receipt} | ConvertTo-Json -Depth 12
}
finally { foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name], 'Process') } }
