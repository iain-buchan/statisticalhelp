[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Artifact,
    [Parameter(Mandatory)][string]$SignToolPath,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$helper = Join-Path $Root 'installer/Sign-Artifact.ps1'
$names = @('SD_HELP_SIGNTOOL_PATH','SD_HELP_CERT_THUMBPRINT','SD_HELP_TIMESTAMP_URL')
$old = @{}
foreach ($name in $names) { $old[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$results = @()
try {
    $env:SD_HELP_SIGNTOOL_PATH = $SignToolPath
    $env:SD_HELP_CERT_THUMBPRINT = $CertificateThumbprint
    $env:SD_HELP_TIMESTAMP_URL = 'http://timestamp.globalsign.com/tsa/r6advanced1'
    $hosts = @((Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'), (Get-Process -Id $PID).Path) | Select-Object -Unique
    foreach ($hostPath in $hosts) {
        & $hostPath -NoProfile -ExecutionPolicy Bypass -File $helper -Path $Artifact -VerifyOnly
        $passed = $LASTEXITCODE -eq 0
        $results += [pscustomobject]@{host=[IO.Path]::GetFileName($hostPath);passed=$passed}
        if (-not $passed) { throw ('Read-only signature verification failed under '+$hostPath) }
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$old[$name],'Process') }
}
New-Item -ItemType Directory -Path (Join-Path $Root 'evidence') -Force | Out-Null
[pscustomobject]@{checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');passed=$true;readOnly=$true;checks=$results} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'evidence/signing-host-checks.json') -Encoding utf8
Write-Host ($results.Count.ToString()+' signing-host checks passed; no file was signed or changed.')
