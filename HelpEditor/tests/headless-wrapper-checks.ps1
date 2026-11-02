param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$wrapper = Join-Path $rootPath 'Build-Help.ps1'
$builder = Join-Path $rootPath 'App\Builder\StatsDirectHelpBuilder.exe'
$runRoot = Join-Path $rootPath ('evidence\headless-wrapper-fixture\run-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $runRoot 'Source copy with spaces'
$output = Join-Path $runRoot 'Generated output with spaces'
$outside = Join-Path $runRoot 'Unrelated working directory'
$reportFile = Join-Path $rootPath 'evidence\headless-wrapper-checks.json'
$checks = [Collections.Generic.List[object]]::new()
$script:passed = 0
$script:failed = 0
$started = [datetime]::UtcNow
$shellPath = (Get-Process -Id $PID).Path

function Check([bool]$Value, [string]$Name, $Detail = $null) {
    if ($Value) { $script:passed++ } else { $script:failed++ }
    $checks.Add([pscustomobject]@{name=$Name;passed=$Value;detail=$Detail})
    $prefix = if ($Value) {'PASS '} else {'FAIL '}
    Write-Host ($prefix + $Name)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Invoke-Headless([string]$Target, [string]$OutputFolder = $output) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $shellPath
    $start.WorkingDirectory = $outside
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile','-NonInteractive','-File',$wrapper,'-Target',$Target,'-ProjectPath',$source,'-OutputRoot',$OutputFolder)) { $start.ArgumentList.Add($argument) }
    # A .NET apphost finds the installed runtime without PATH. Deliberately make
    # the SDK's dotnet command unavailable: ordinary builds must not invoke it.
    $start.Environment['PATH'] = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); throw 'Headless wrapper did not finish within two minutes.' }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        $json = $null
        if ($process.ExitCode -eq 0) { $json = $text | ConvertFrom-Json }
        return [pscustomobject]@{Code=$process.ExitCode;Output=$text;Error=$errorText;Json=$json}
    } finally { $process.Dispose() }
}
function Write-Utf8([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false)) }
function Snapshot([string]$Folder) {
    $value = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Folder -File -Recurse) { $value[$file.FullName] = Hash $file.FullName }
    return $value
}
function Snapshot-Same($Snapshot) {
    foreach ($entry in $Snapshot.GetEnumerator()) { if (-not (Test-Path -LiteralPath $entry.Key) -or (Hash $entry.Key) -ne $entry.Value) { return $false } }
    return $true
}

New-Item -ItemType Directory -Path (Split-Path $reportFile -Parent),$source,$outside,(Join-Path $source 'Content\basics'),(Join-Path $source 'Project\TOCs') -Force | Out-Null
Write-Utf8 $reportFile (@{completed=$false;startedAtUtc=$started.ToString('o');passed=0;failed=0;fixture=$runRoot} | ConvertTo-Json)
try {
    if (-not (Test-Path -LiteralPath $builder)) { throw 'Publish App/Builder/StatsDirectHelpBuilder.exe before running these checks.' }
    Write-Utf8 (Join-Path $source '.statsdirect-help-editor.json') '{"purpose":"isolated headless wrapper verification"}'
    Write-Utf8 (Join-Path $source 'Content\contents.htm') '<html><head><title>Fixture contents</title></head><body><h1>Fixture contents</h1><a href="basics/page.htm#Section">Page</a></body></html>'
    $pageFile = Join-Path $source 'Content\basics\page.htm'
    $initialPage = '<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"><head><title>Headless topic</title></head><body><h1 id="Section">Headless topic</h1><p>HEADLESS_INITIAL_NEEDLE</p><p MadCap:conditions="Primary.SWHELP">DESKTOP_CONDITION_ONLY</p><p MadCap:conditions="Primary.WEBHELP">WEBSITE_CONDITION_ONLY</p><MadCap:equation>$x+1$</MadCap:equation><MadCap:dropDown class="rcode"><MadCap:dropDownHead>R equivalent</MadCap:dropDownHead><MadCap:dropDownBody><pre>x &lt;- 1:3&#10;mean(x)</pre></MadCap:dropDownBody></MadCap:dropDown></body></html>'
    Write-Utf8 $pageFile $initialPage
    $tocFile = Join-Path $source 'Project\TOCs\STATSDIRECT.fltoc'
    Write-Utf8 $tocFile '<CatapultToc><TocEntry Title="Contents" Link="/Content/contents.htm"/><TocEntry Title="Original navigation label" Link="/Content/basics/page.htm#Section"/></CatapultToc>'
    $sourceBefore = Snapshot $source
    $guiPids = @(Get-Process StatsDirectHelpEditor -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

    $all = Invoke-Headless 'All'
    Check ($all.Code -eq 0) 'All outputs build from an unrelated working directory with no dotnet command on PATH' $all.Error
    if ($all.Code -ne 0) { throw 'The first headless build failed; later assertions would be misleading.' }
    Check (@($all.Json.Outputs).Count -eq 2) 'All builds each distinct Desktop/Public payload exactly once'
    Check ($all.Json.SourceProject -eq $source -and $all.Json.Builder -eq $builder) 'Machine-readable output identifies the saved source and packaged console builder'
    $desktopInfo = @($all.Json.Outputs | Where-Object { $_.OutputDirectory -eq (Join-Path $output 'Desktop') })[0]
    $publicInfo = @($all.Json.Outputs | Where-Object { $_.OutputDirectory -eq (Join-Path $output 'Public') })[0]
    Check ($desktopInfo.ConsumerTargets -is [array] -and @($desktopInfo.ConsumerTargets).Count -eq 1 -and $desktopInfo.ConsumerTargets[0] -eq 'DesktopHelp' -and $publicInfo.ConsumerTargets -is [array] -and ($publicInfo.ConsumerTargets -join ',') -eq 'Website,GitHub') 'Consumer target labels use consistent JSON arrays, including the single DesktopHelp label'
    Check ($all.Json.Published -eq $false) 'Successful compilation explicitly reports that nothing was published'
    Check (@($all.Json.Outputs | Where-Object { $_.TopicCount -ne 2 -or $_.SearchDocumentCount -ne 2 -or @($_.Warnings).Count -ne 0 }).Count -eq 0) 'Both targets report two topics, rebuilt search and no warnings'
    $desktop = Join-Path $output 'Desktop'
    $public = Join-Path $output 'Public'
    $desktopText = Get-Content -LiteralPath (Join-Path $desktop 'basics\page.htm') -Raw
    $publicText = Get-Content -LiteralPath (Join-Path $public 'basics\page.htm') -Raw
    Check ($desktopText.Contains('DESKTOP_CONDITION_ONLY') -and -not $desktopText.Contains('WEBSITE_CONDITION_ONLY') -and $publicText.Contains('WEBSITE_CONDITION_ONLY') -and -not $publicText.Contains('DESKTOP_CONDITION_ONLY')) 'Headless Desktop and Public conditions remain distinct'
    Check ($publicText.Contains('help-equation') -and $publicText.Contains('<details') -and $publicText.Contains('mean(x)') -and (Test-Path -LiteralPath (Join-Path $public 'resources\help-editor-runtime\mathjax\tex-mml-svg.js'))) 'Offline equations, R sections and code survive the console build'
    Check (Snapshot-Same $sourceBefore) 'The first headless build changes no authored source files'

    $desktopReceipt = Hash (Join-Path $desktop 'build-info.json')
    Write-Utf8 $pageFile ($initialPage.Replace('HEADLESS_INITIAL_NEEDLE','HEADLESS_UPDATED_NEEDLE'))
    Write-Utf8 $tocFile ((Get-Content -LiteralPath $tocFile -Raw).Replace('Original navigation label','Updated navigation label'))
    $updatedSource = Snapshot $source
    $website = Invoke-Headless 'Website'
    Check ($website.Code -eq 0 -and @($website.Json.Outputs).Count -eq 1 -and $website.Json.Outputs[0].OutputDirectory -eq $public) 'Website alias rebuilds only the Public output'
    $html = Get-Content -LiteralPath (Join-Path $public 'basics\page.htm') -Raw
    $search = Get-Content -LiteralPath (Join-Path $public 'search-index.js') -Raw
    $toc = Get-Content -LiteralPath (Join-Path $public 'contents-data.js') -Raw
    Check ($html.Contains('HEADLESS_UPDATED_NEEDLE') -and -not $html.Contains('HEADLESS_INITIAL_NEEDLE') -and $search.Contains('HEADLESS_UPDATED_NEEDLE') -and -not $search.Contains('HEADLESS_INITIAL_NEEDLE') -and $toc.Contains('Updated navigation label')) 'Raw XHTML and Contents edits regenerate topic text, search and navigation without the GUI'
    Check ((Hash (Join-Path $desktop 'build-info.json')) -eq $desktopReceipt) 'A Website-only rebuild leaves Desktop output untouched'
    $github = Invoke-Headless 'GitHub'
    Check ($github.Code -eq 0 -and @($github.Json.Outputs).Count -eq 1 -and $github.Json.Outputs[0].OutputDirectory -eq $public -and -not (Test-Path -LiteralPath (Join-Path $output 'GitHub'))) 'GitHub alias reuses the Public payload without publishing or creating a redundant third tree'
    $desktopAlias = Invoke-Headless 'DesktopHelp'
    Check ($desktopAlias.Code -eq 0 -and @($desktopAlias.Json.Outputs).Count -eq 1 -and $desktopAlias.Json.Outputs[0].OutputDirectory -eq $desktop) 'DesktopHelp alias builds the Desktop output'
    Check (Snapshot-Same $updatedSource) 'Repeated alias builds preserve every intentionally edited source byte'

    $beforeFailure = Snapshot $public
    Write-Utf8 $pageFile '<html><body>unfinished'
    $invalid = Invoke-Headless 'Public'
    Check ($invalid.Code -ne 0 -and -not [string]::IsNullOrWhiteSpace($invalid.Error)) 'Invalid raw XHTML reports an error and a nonzero process exit code'
    Check (Snapshot-Same $beforeFailure) 'A malformed-source failure preserves the previous complete output'
    Write-Utf8 $pageFile ($initialPage.Replace('HEADLESS_INITIAL_NEEDLE','HEADLESS_UPDATED_NEEDLE'))
    $both = Invoke-Headless 'Both'
    Check ($both.Code -eq 0 -and @($both.Json.Outputs).Count -eq 2) 'Both remains a compatible alias and a corrected source rebuild recovers normally'
    $afterGuiPids = @(Get-Process StatsDirectHelpEditor -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    Check (@(Compare-Object $guiPids $afterGuiPids).Count -eq 0) 'Console compilation starts no help-editor GUI process and leaves existing GUI processes alone'
} catch {
    Check $false 'Headless wrapper verification completed' $_.Exception.ToString()
} finally {
    [pscustomobject]@{completed=$true;checkedAtUtc=[datetime]::UtcNow.ToString('o');passed=$script:passed;failed=$script:failed;fixture=$runRoot;sourceProject=$source;outputs=$output;guiStarted=$false;usesSdk=$false;checks=$checks} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding utf8
}
Write-Host ($script:passed.ToString() + ' headless wrapper checks passed; ' + $script:failed + ' failed')
if ($script:failed -ne 0) { throw 'Headless wrapper checks failed. See evidence/headless-wrapper-checks.json.' }
