[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$tool = $env:SD_HELP_SIGNTOOL_PATH
$thumbprint = $env:SD_HELP_CERT_THUMBPRINT
$timestamp = $env:SD_HELP_TIMESTAMP_URL
if (-not $tool -or -not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Set SD_HELP_SIGNTOOL_PATH to the Windows SDK SignTool executable.' }
if ($thumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw 'A code-signing certificate thumbprint is required.' }
if (-not $timestamp) { throw 'An RFC3161 timestamp URL is required.' }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'The artifact to sign does not exist.' }
& $tool sign /fd SHA256 /tr $timestamp /td SHA256 /sha1 $thumbprint $Path
if ($LASTEXITCODE -ne 0) { throw ('Signing failed: ' + (Split-Path -Leaf $Path)) }
& $tool verify /pa /v $Path
if ($LASTEXITCODE -ne 0) { throw ('Signature verification failed: ' + (Split-Path -Leaf $Path)) }
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint -or -not $signature.TimeStamperCertificate) {
    throw ('The signed artifact does not have the required valid timestamped signature: ' + (Split-Path -Leaf $Path))
}
