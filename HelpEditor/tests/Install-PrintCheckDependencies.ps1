#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [string]$PythonPath = 'python'
)

# Developer/CI dependencies only. Nothing is installed globally or shipped.
# Pins match the pypdf/Poppler versions used by the local print regression gate.
# Package digest sources: https://pypi.org/pypi/pypdf/6.10.0/json and
# https://api.github.com/repos/oschwartz10612/poppler-windows/releases/tags/v26.07.0-0
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The print-check dependency package is for Windows.' }
$destination = [IO.Path]::GetFullPath($DestinationDirectory).TrimEnd('\', '/')
if ($destination -eq [IO.Path]::GetPathRoot($destination).TrimEnd('\', '/')) {
    throw 'Choose a separate temporary dependency directory, not a drive root.'
}
if (Test-Path -LiteralPath $destination) {
    throw 'Choose a fresh dependency directory; existing files are never overwritten.'
}
$python = (Get-Command $PythonPath -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pypdfVersion = '6.10.0'
$wheelUrl = 'https://files.pythonhosted.org/packages/55/f2/7ebe366f633f30a6ad105f650f44f24f98cb1335c4157d21ae47138b3482/pypdf-6.10.0-py3-none-any.whl'
$wheelSha256 = '90005e959e1596c6e6c84c8b0ad383285b3e17011751cedd17f2ce8fcdfc86de'
$popplerVersion = '26.07.0'
$popplerUrl = 'https://github.com/oschwartz10612/poppler-windows/releases/download/v26.07.0-0/Release-26.07.0-0.zip'
$popplerSha256 = 'a711b0563b06edc488583d28198b6734c5a494afbbd1b9d87d3d2866062fb7e2'

function Invoke-PrintTool([string]$Executable, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            throw ('Dependency tool timed out: ' + [IO.Path]::GetFileName($Executable))
        }
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw ('Dependency tool failed: ' + $text) }
        return $text.Trim()
    }
    finally { $process.Dispose() }
}

function Get-VerifiedDownload([string]$Url, [string]$File, [string]$Sha256) {
    Invoke-WebRequest -Uri $Url -OutFile $File -TimeoutSec 120 -MaximumRetryCount 2 -RetryIntervalSec 2
    if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Sha256) {
        throw ('Downloaded dependency checksum differs: ' + [IO.Path]::GetFileName($File))
    }
}

$pythonVersion = Invoke-PrintTool $python @('-c', 'import sys; print(".".join(map(str,sys.version_info[:3])))')
if ($pythonVersion -notmatch '^3\.12\.\d+$') { throw ('Use Python 3.12 for these checks; found ' + $pythonVersion) }
New-Item -ItemType Directory -Path $destination | Out-Null

Write-Host 'Preparing isolated, pinned print-check dependencies.'
$venv = Join-Path $destination 'python'
Invoke-PrintTool $python @('-m', 'venv', $venv) | Out-Null
$testPython = Join-Path $venv 'Scripts\python.exe'
$wheel = Join-Path $destination 'pypdf-6.10.0-py3-none-any.whl'
Get-VerifiedDownload $wheelUrl $wheel $wheelSha256
Write-Host (Invoke-PrintTool $testPython @('-m', 'pip', 'install', '--disable-pip-version-check', '--no-index', '--no-deps', $wheel))
if ((Invoke-PrintTool $testPython @('-c', 'import pypdf; print(pypdf.__version__)')) -ne $pypdfVersion) {
    throw 'The isolated Python did not load the pinned pypdf version.'
}

$archive = Join-Path $destination 'Release-26.07.0-0.zip'
Get-VerifiedDownload $popplerUrl $archive $popplerSha256
$expanded = Join-Path $destination 'poppler'
Expand-Archive -LiteralPath $archive -DestinationPath $expanded
$popplerBin = Join-Path $expanded 'poppler-26.07.0\Library\bin'
foreach ($tool in @('pdfinfo', 'pdftoppm')) {
    $versionText = Invoke-PrintTool (Join-Path $popplerBin ($tool + '.exe')) @('-v')
    if ($versionText -notmatch ('(?m)^' + $tool + ' version ' + [regex]::Escape($popplerVersion) + '\s*$')) {
        throw ('Unexpected Poppler version: ' + $versionText)
    }
}

$result = [pscustomobject]@{
    PythonPath = $testPython
    PythonVersion = $pythonVersion
    PypdfVersion = $pypdfVersion
    PypdfWheelSha256 = $wheelSha256
    PopplerBin = $popplerBin
    PopplerVersion = $popplerVersion
    PopplerArchiveSha256 = $popplerSha256
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'dependencies.json') -Encoding utf8
$result
