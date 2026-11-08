[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path, [switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
# WiX launches Windows PowerShell even when the build starts in PowerShell 7.
# Select this host's own security module rather than an incompatible inherited
# PSModulePath entry. Import before signing so a host problem fails early.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
$tool = $env:SD_HELP_SIGNTOOL_PATH
$thumbprint = $env:SD_HELP_CERT_THUMBPRINT
$timestamp = $env:SD_HELP_TIMESTAMP_URL
# Microsoft Artifact Signing: SignTool's Artifact Signing plug-in, the account and certificate profile in its metadata file,
# and the identity-specific EKU that every certificate issued for the validated publisher carries, since its short-lived
# certificates (and so their thumbprints) change every few days.
$dlib = $env:SD_HELP_SIGNING_DLIB
$metadata = $env:SD_HELP_SIGNING_METADATA
$identity = $env:SD_HELP_SIGNER_IDENTITY_EKU
$artifactSigning = -not [string]::IsNullOrEmpty($metadata)
if (-not $tool -or -not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Set SD_HELP_SIGNTOOL_PATH to the Windows SDK SignTool executable.' }
if ($artifactSigning) {
    if (-not $dlib -or -not (Test-Path -LiteralPath $dlib -PathType Leaf)) { throw 'Set SD_HELP_SIGNING_DLIB to the Artifact Signing client Azure.CodeSigning.Dlib.dll.' }
    if (-not (Test-Path -LiteralPath $metadata -PathType Leaf)) { throw 'The Artifact Signing metadata file does not exist.' }
    if ($identity -notmatch '^1\.3\.6\.1\.4\.1\.311\.97\.\d+(\.\d+)+$') { throw 'Artifact Signing needs the expected publisher identity EKU (SD_HELP_SIGNER_IDENTITY_EKU).' }
}
elseif ($thumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw 'A code-signing certificate thumbprint is required.' }
if (-not $timestamp) { throw 'An RFC3161 timestamp URL is required.' }
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'The artifact to sign does not exist.' }
if (-not $VerifyOnly) {
    if ($artifactSigning) { & $tool sign /fd SHA256 /tr $timestamp /td SHA256 /dlib $dlib /dmdf $metadata $Path }
    else { & $tool sign /fd SHA256 /tr $timestamp /td SHA256 /sha1 $thumbprint $Path }
    if ($LASTEXITCODE -ne 0) { throw ('Signing failed: ' + (Split-Path -Leaf $Path)) }
}
& $tool verify /pa /v $Path
if ($LASTEXITCODE -ne 0) { throw ('Signature verification failed: ' + (Split-Path -Leaf $Path)) }
$signature = Get-AuthenticodeSignature -LiteralPath $Path
$signer = $signature.SignerCertificate
$expectedSigner = $false
if ($signer) {
    if ($artifactSigning) {
        $usages = @($signer.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
        $expectedSigner = $usages -contains $identity
    }
    else { $expectedSigner = $signer.Thumbprint -eq $thumbprint }
}
if ($signature.Status -ne 'Valid' -or -not $expectedSigner -or -not $signature.TimeStamperCertificate) {
    throw ('The signed artifact does not have the required valid timestamped signature: ' + (Split-Path -Leaf $Path))
}
