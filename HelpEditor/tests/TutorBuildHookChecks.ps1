param([Parameter(Mandatory=$true)][string]$StatsDirectSourceRoot)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $root ('evidence\TutorBuildHook-'+[Guid]::NewGuid().ToString('N'))
$learning=Join-Path $fixture 'Learning'
New-Item -ItemType Directory -Path $learning -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $StatsDirectSourceRoot 'Learning\AuthoredTutor.targets') -Destination (Join-Path $learning 'AuthoredTutor.targets')
$project=Join-Path $fixture 'Hook.proj'
@'
<Project>
  <Import Project="Learning/AuthoredTutor.targets" />
  <Target Name="BeforeBuild"><WriteLinesToFile File="$(MSBuildProjectDirectory)/before-build.txt" Lines="native-build-reached" Overwrite="true" /></Target>
  <Target Name="Build" DependsOnTargets="BeforeBuild" />
</Project>
'@ | Set-Content -LiteralPath $project
$checks=[System.Collections.Generic.List[object]]::new()
function Check([bool]$Passed,[string]$Name){$checks.Add([pscustomobject]@{passed=$Passed;name=$Name});Write-Host "$(if($Passed){'PASS'}else{'FAIL'}) $Name"}
function Run([string]$Name,[string[]]$Extra=@()){
    $before=Join-Path $fixture 'before-build.txt'
    if(Test-Path -LiteralPath $before){Remove-Item -LiteralPath $before}
    $output=& dotnet msbuild $project -target:Build -nologo -verbosity:minimal @Extra 2>&1
    $code=$LASTEXITCODE
    $output | Out-String | Set-Content -LiteralPath (Join-Path $fixture ($Name+'.log'))
    return [pscustomobject]@{code=$code;text=($output -join "`n");nativeReached=(Test-Path -LiteralPath $before)}
}
$result=Run 'no-authored-input'
Check ($result.code -eq 0 -and $result.nativeReached) 'A project without authored tutor inputs can build without Node dependencies'
$authored=Join-Path $learning 'AuthoredTutor'
New-Item -ItemType Directory -Path $authored | Out-Null
'{}' | Set-Content -LiteralPath (Join-Path $authored 'manifest.json')
$result=Run 'missing-dependencies'
Check ($result.code -ne 0 -and -not $result.nativeReached -and $result.text.Contains('pnpm --dir Learning install --frozen-lockfile --ignore-scripts')) 'Authored inputs stop the native build with the correct pinned dependency command'
Remove-Item -LiteralPath (Join-Path $authored 'manifest.json')
'incomplete publication' | Set-Content -LiteralPath (Join-Path $learning '.AuthoredTutor.publishing')
$result=Run 'marker-without-manifest'
Check ($result.code -ne 0 -and -not $result.nativeReached) 'A publication marker stops the build even while its manifest is absent'
$result=Run 'design-time' @('-property:DesignTimeBuild=true')
Check ($result.code -eq 0 -and $result.nativeReached) 'Visual Studio design-time evaluation does not run the authored build'
Remove-Item -LiteralPath (Join-Path $learning '.AuthoredTutor.publishing')
'{}' | Set-Content -LiteralPath (Join-Path $authored 'manifest.json')
$dependency=Join-Path $learning 'node_modules\esbuild'
New-Item -ItemType Directory -Path $dependency -Force | Out-Null
'{}' | Set-Content -LiteralPath (Join-Path $dependency 'package.json')
@'
import fs from 'node:fs';
if(process.argv[2]!=='--authored-if-needed') throw Error('Missing authored build argument');
fs.writeFileSync('hook-executed.json',JSON.stringify({argument:process.argv[2],cwd:process.cwd()}));
process.exit(17);
'@ | Set-Content -LiteralPath (Join-Path $learning 'build.mjs')
$result=Run 'authored-builder-failure'
$probe=Get-Content -LiteralPath (Join-Path $learning 'hook-executed.json') -Raw | ConvertFrom-Json
Check ($result.code -ne 0 -and -not $result.nativeReached -and $probe.argument -eq '--authored-if-needed' -and $probe.cwd -eq $learning) 'A failed authored builder runs in Learning and prevents native compilation'
$report=[pscustomobject]@{passed=(@($checks | Where-Object {-not $_.passed}).Count -eq 0);checks=$checks;fixture=$fixture;checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'evidence\tutor-build-hook-checks.json')
if(-not $report.passed){exit 1}
