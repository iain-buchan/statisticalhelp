[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory, [Parameter(Mandatory)][string]$OutputFile)
$ErrorActionPreference = 'Stop'
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
function StableId([string]$Value) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())))).Replace('-', '').Substring(0, 24) }
    finally { $hash.Dispose() }
}
function StableGuid([string]$Value) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $hash.ComputeHash([Text.Encoding]::UTF8.GetBytes('StatsDirect.HelpEditor.Installer/' + $Value.ToLowerInvariant()))[0..15]; ([Guid]::new([byte[]]$bytes)).ToString('D') }
    finally { $hash.Dispose() }
}
function Escape([string]$Value) { [Security.SecurityElement]::Escape($Value) }
function DirectoryId([string]$Value) { if (-not $Value) { 'INSTALLFOLDER' } else { 'dir' + (StableId $Value) } }
$files = @(Get-ChildItem -LiteralPath $PayloadDirectory -File -Recurse | Sort-Object FullName)
if (-not $files.Count) { throw 'The installer payload is empty.' }
$directories = @{}
foreach ($file in $files) {
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Installer payload must not contain linked files.' }
    $relative = $file.FullName.Substring($PayloadDirectory.TrimEnd('\').Length + 1)
    $folder = Split-Path -Parent $relative
    while ($folder) { $directories[$folder] = $true; $folder = Split-Path -Parent $folder }
}
$xml = [Text.StringBuilder]::new()
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
foreach ($folder in @($directories.Keys | Sort-Object)) {
    $parent = Split-Path -Parent $folder
    [void]$xml.AppendLine(('<DirectoryRef Id="{0}"><Directory Id="{1}" Name="{2}" /></DirectoryRef>' -f (DirectoryId $parent), (DirectoryId $folder), (Escape (Split-Path -Leaf $folder))))
}
[void]$xml.AppendLine('<ComponentGroup Id="HelpEditorPayload">')
foreach ($file in $files) {
    $relative = $file.FullName.Substring($PayloadDirectory.TrimEnd('\').Length + 1)
    $id = StableId $relative
    $fileId = if ($relative -eq 'StatsDirectHelpEditor.exe') { 'HelpEditorExe' } else { 'file' + $id }
    [void]$xml.AppendLine(('<Component Id="cmp{0}" Directory="{1}" Guid="{2}">' -f $id, (DirectoryId (Split-Path -Parent $relative)), (StableGuid $relative)))
    [void]$xml.AppendLine(('<File Id="{0}" Source="$(var.PayloadDirectory)\{1}" />' -f $fileId, (Escape $relative)))
    # Per-user profile components use HKCU key paths (Windows Installer ICE38).
    [void]$xml.AppendLine(('<RegistryValue Root="HKCU" Key="Software\StatsDirect\HelpEditor\Installer\Files" Name="{0}" Type="integer" Value="1" KeyPath="yes" />' -f $id))
    [void]$xml.AppendLine('</Component>')
}
foreach ($folder in @('') + @($directories.Keys | Sort-Object)) {
    $id = StableId ('directory/' + $folder)
    [void]$xml.AppendLine(('<Component Id="cleanup{0}" Directory="{1}" Guid="{2}"><RemoveFolder Id="remove{0}" On="uninstall" /><RegistryValue Root="HKCU" Key="Software\StatsDirect\HelpEditor\Installer\Directories" Name="{0}" Type="integer" Value="1" KeyPath="yes" /></Component>' -f $id, (DirectoryId $folder), (StableGuid ('directory/' + $folder))))
}
[void]$xml.AppendLine('</ComponentGroup></Fragment></Wix>')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputFile), $xml.ToString(), [Text.UTF8Encoding]::new($false))
