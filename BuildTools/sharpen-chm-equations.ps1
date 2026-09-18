<#
sharpen-chm-equations.ps1
=========================
Redraws the equation pictures of a Flare HTML Help (CHM) build with more picture dots, then
compiles the CHM again. Run by the post-build event of the SWHelp target:

    ver >NUL 2>NUL
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(ProjectDirectory)BuildTools\sharpen-chm-equations.ps1" -OutputDirectory "$(OutputDirectory)." -TargetName "$(TargetName)"

  (The first line is a throwaway and must stay. Flare's builder may put a byte-order mark in
   front of the first command, which then fails; it does so when the console it was started from
   uses the UTF-8 code page and not otherwise, so the line may well be seen to succeed. Either
   way it does nothing. The full stop after $(OutputDirectory) is there because the variable ends
   in a backslash, and a backslash before a closing quote would swallow the quote.)

WHY
  For HTML Help, Flare 22 turns each equation into a PNG with exactly one dot per CSS pixel. On a
  display scaled to 150% or 250% the help viewer has to stretch that picture, so equations look soft.
  Flare 22 also ignores the stylesheet's font size and draws every equation at 6 pixels per "ex";
  Flare up to version 20 honoured the stylesheet (MadCap|equation at 120% of 10pt, which is 8 pixels
  per ex), so the equations have also become a quarter smaller than intended. Flare has no setting
  for either.

HOW
  Flare keeps the vector drawing (SVG, made by MathJax) of every equation next to the PNG in its
  equation cache. The cache file names are hashes of the formula and font settings, not of the
  picture, so a picture is matched to its drawing by finding the cached PNG with the same bytes.
  The drawing is then painted again with the same library Flare uses (Svg.Skia, loaded from the
  Flare program folder) at -Density dots per CSS pixel, and the width and height in the topic's
  <img> tag are set to the CSS size. The help viewer script chm-pictures.js already shows every
  picture at its styled size times the display scaling.

HOW MANY DOTS (-Density)
  2, and not more. The help viewer (the old Internet Explorer engine) shrinks a picture by looking
  at only the 2 x 2 dots around each screen pixel. While a picture is shrunk by no more than 2 to 1
  every dot still counts, and at exactly 2 to 1 (a display at 100%) the result is a true average.
  Shrunk 3 to 1 it takes one dot in three and drops the rest: seen in the viewer, the "i=1" under
  a summation sign then reads "i-1", because a thin bar of the "=" falls between the dots taken.
  A display at 100% shrinks a picture by exactly -Density to 1, so with 3 that happens on every
  display at 100% (and at 125%, 2.4 to 1, some dots are skipped as well). With 2 a picture is
  sharp up to 200% scaling and only slightly soft beyond (at 250% it is stretched 1.25 times,
  where Flare's own picture is stretched 2.5 times).

COST
  More dots make bigger pictures and a bigger CHM: with the defaults this help's CHM grows from
  4.0 MB to 5.8 MB.

SAFETY
  Whatever goes wrong, the CHM that Flare built is left in the output folder as it was, the line
  "EQUATIONS NOT SHARPENED: <reason>" goes to the build log, and the script ends with exit code 0.
  (Flare's builder carries on and publishes whatever a post-build command returns, so the exit
  code is a courtesy; the words to search a build log for are "EQUATIONS NOT SHARPENED", and
  "Run Command Error" for a command that could not be started at all.)
  The compiled CHM is accepted only if the help compiler reported every file compiled.
  Pictures and topics are changed together or not at all: if one of them cannot be written, those
  already written are put back. (While they are being written the file sharpen-chm-equations.writing
  exists beside the log; a run that finds it there knows that an earlier run was cut short, and
  refuses to compile what may be a mixture.)
  A second run on the same build changes nothing; only if the first run redrew the pictures and
  then failed at the compile does it compile again. To redraw with other settings, build the
  target again in Flare first: that makes fresh pictures.
  Beside the log the script keeps Flare's own CHM as <name>.chm.as-flare-built.
  The log, <output>\..\Temporary\<target>\sharpen-chm-equations.log, is added to by every run and
  starts afresh with every build, because Flare empties that folder. It is kept out of the output
  folder because everything in the output folder gets published.

PARAMETERS
  -OutputDirectory  Flare's output folder for the target (where the finished CHM is).
  -TargetName       Name of the target; default: the name of the output folder.
  -PixelsPerEx      Size of the equations. 8 is what Flare up to version 20 gave this stylesheet;
                    6 reproduces the size Flare 22 draws.
  -Density          Picture dots per CSS pixel; a whole number. See HOW MANY DOTS above.
  -FlareApp         The Flare.app folder; default: the Flare that is running this build, or else
                    the newest one under Program Files.
  -CacheFolder      Flare's equation cache; default: %TEMP%\MadCap Software\Flare\Equations.
#>
param(
    [string]$OutputDirectory = '',
    [string]$TargetName = '',
    [string]$PixelsPerEx = '8',     # the two numbers are taken as text and read further down, so that
    [string]$Density = '2',         # a slip in the command line is reported like any other failure
    [string]$FlareApp = '',
    [string]$CacheFolder = ''
)

$ErrorActionPreference = 'Stop'
$script:Unknown = $args          # anything on the command line that is not one of the parameters above
$script:LogLines = New-Object System.Collections.Generic.List[string]
$script:LogFile = ''

# Say: to the build log (standard output) and to our own log. Note: to our own log only.
function Say([string]$text) { Write-Host "sharpen-chm-equations: $text"; $script:LogLines.Add($text) }
function Note([string]$text) { $script:LogLines.Add($text) }

# For the build log: the first few names only; our own log gets them all.
function Join-Names([string[]]$names) {
    $sorted = @($names | Sort-Object)
    if ($sorted.Count -le 8) { return ($sorted -join ', ') }
    return (($sorted[0..7] -join ', ') + " and $($sorted.Count - 8) more (all named in $($script:LogFile))")
}

# PowerShell wraps an error from .NET in words of its own ('Exception calling "Copy" with "3"
# argument(s)'); the innermost message is the one that says what happened.
function Get-Reason($errorRecord) {
    $e = $errorRecord.Exception
    while ($e.InnerException) { $e = $e.InnerException }
    return $e.Message
}

# Topics are read and written through Latin-1, which maps every byte to one character and back.
# That way a file's encoding, byte-order mark and line endings survive untouched whatever they
# are; the <img> tags we edit are plain ASCII in any encoding Flare writes.
$script:Latin1 = [Text.Encoding]::GetEncoding(28591)

# An equation's <img> tag, with the picture's file name as group 1.
$script:ImgPattern = '(?i)<img\b[^>]*?\bsrc="[^"]*GeneratedImages/Equations/([^"/]+\.png)"[^>]*>'

# A redrawn picture must hold about as much ink as Flare's, allowing for its size (see Get-Ink).
# Over this help's 258 equations, tried at six sizes and densities, the ratio ran from 0.94 to
# 1.07. A drawing that came out blank, at the wrong scale or without most of its symbols falls
# far outside these limits; one symbol lost from a long formula would not be noticed.
$script:InkLowest = 0.85
$script:InkHighest = 1.15


function Find-FlareApp {
    # Prefer the Flare that is running this build: several versions can be installed side by side,
    # and the pictures should be drawn by the same library version that drew the originals.
    # The post-build event runs as madbuild.exe -> cmd.exe -> powershell.exe, so walk up the
    # parents of this process and take the first whose folder holds Svg.Skia.dll.
    try {
        $id = $PID
        for ($i = 0; $i -lt 8 -and $id; $i++) {
            $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id"
            if (-not $p) { break }
            if ($p.ExecutablePath) {
                $folder = Split-Path $p.ExecutablePath -Parent
                if (Test-Path -LiteralPath (Join-Path $folder 'Svg.Skia.dll')) {
                    Note "Flare found as the program running this build: $($p.ExecutablePath)"
                    return $folder
                }
            }
            $id = $p.ParentProcessId
        }
    } catch { }   # asking Windows about processes can fail; the fallback below will do

    # Otherwise (run by hand): the installed Flare with the highest version number in its name.
    $root = Join-Path $env:ProgramFiles 'MadCap Software'
    $best = $null; $bestNumber = -1.0
    foreach ($dir in @(Get-ChildItem -LiteralPath $root -Directory -Filter 'MadCap Flare*' -ErrorAction SilentlyContinue)) {
        $app = Join-Path $dir.FullName 'Flare.app'
        if (-not (Test-Path -LiteralPath (Join-Path $app 'Svg.Skia.dll'))) { continue }
        $number = 0.0
        if ($dir.Name -match '(\d+(\.\d+)?)\s*$') { $number = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
        if ($number -gt $bestNumber) { $best = $app; $bestNumber = $number }
    }
    if ($best) { Note "Flare found as the newest installed under $root" }
    return $best
}


function Initialize-Skia([string]$app) {
    # Windows PowerShell runs on .NET Framework without Flare's "binding redirects", so when
    # SkiaSharp asks for System.Memory 4.0.1.2 and Flare ships 4.0.5.0, .NET refuses. The small
    # handler below answers every such request with the file of that name in the Flare folder,
    # whatever its version. It is compiled C# rather than a PowerShell script block because .NET
    # may call it on a thread where PowerShell cannot run. (PictureInk is C# only for speed:
    # adding up millions of dots one by one is slow in PowerShell.)
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class FlareAssemblyResolver {
    static string folder;
    static bool installed;
    public static void Install(string flareApp) {
        folder = flareApp;
        if (installed) return;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        installed = true;
    }
    static Assembly Resolve(object sender, ResolveEventArgs e) {
        string path = Path.Combine(folder, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
public static class PictureInk {
    // The opacity of every dot added up. Each dot is 4 bytes and opacity is the last of them.
    public static long AlphaSum(byte[] pixels) {
        long sum = 0;
        for (int i = 3; i < pixels.Length; i += 4) sum += pixels[i];
        return sum;
    }
}
'@
    [FlareAssemblyResolver]::Install($app)

    # SkiaSharp.dll is only a wrapper; the drawing is done by the native libSkiaSharp.dll, which
    # Windows looks for along PATH.
    $env:PATH = "$app;$env:PATH"

    [void][Reflection.Assembly]::LoadFrom((Join-Path $app 'SkiaSharp.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $app 'Svg.Skia.dll'))
}


function Get-ContentHash([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash($bytes)) } finally { $sha.Dispose() }
}


function Get-PngSize([byte[]]$bytes) {
    # Every PNG starts with an 8-byte signature and the IHDR chunk; width and height are the
    # big-endian 32-bit numbers at bytes 16-19 and 20-23. Returns $null if this is not a PNG.
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) { return $null }
    $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return @{ Width = $w; Height = $h }
}


function Get-Ink([byte[]]$pngBytes) {
    # How much ink a PNG holds: the opacity of all its dots added up. Reading the finished PNG
    # back, rather than measuring the drawing in memory, also proves that the file can be read.
    $bitmap = [SkiaSharp.SKBitmap]::Decode($pngBytes)
    if ($null -eq $bitmap) { throw 'the PNG cannot be read back' }
    try {
        if ($bitmap.BytesPerPixel -ne 4) { throw 'the PNG is not a 32-bit picture' }
        return [PictureInk]::AlphaSum($bitmap.Bytes)
    } finally { $bitmap.Dispose() }
}


function Get-StyledSize([string]$imgTag) {
    # The width and height (whole CSS pixels) in the tag's style attribute, or $null.
    $style = [regex]::Match($imgTag, '(?i)\bstyle="([^"]*)"')
    if (-not $style.Success) { return $null }
    $w = [regex]::Match($style.Groups[1].Value, '(?i)(?<![-\w])width:\s*(\d+)px')
    $h = [regex]::Match($style.Groups[1].Value, '(?i)(?<![-\w])height:\s*(\d+)px')
    if (-not ($w.Success -and $h.Success)) { return $null }
    return @{ Width = [int]$w.Groups[1].Value; Height = [int]$h.Groups[1].Value }
}


function Get-SvgSizeInEx([string]$svgText) {
    # MathJax gives the drawing's size in ex units on the root element, e.g. width="19.24ex".
    $root = [regex]::Match($svgText, '<svg\b[^>]*>')
    if (-not $root.Success) { throw 'no <svg> element' }
    $w = [regex]::Match($root.Value, '\swidth="([0-9.]+)ex"')
    $h = [regex]::Match($root.Value, '\sheight="([0-9.]+)ex"')
    if (-not ($w.Success -and $h.Success)) { throw 'the drawing''s width and height are not in ex units' }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $size = @{ Width = [double]::Parse($w.Groups[1].Value, $inv); Height = [double]::Parse($h.Groups[1].Value, $inv) }
    if ($size.Width -le 0 -or $size.Height -le 0) { throw 'the drawing has no size' }
    return $size
}


function Convert-SvgToPng([string]$svgText, [int]$pixelWidth, [int]$pixelHeight) {
    # Left to itself Svg.Skia takes 1 ex = 6 pixels whatever the font size. So tell the drawing
    # how big to be: write the width and height we want, in whole pixels, over the ex values on
    # the root element. Svg.Skia then fits the drawing inside that box, keeping its proportions
    # and centring it, exactly as it does for Flare's own pictures (only with more dots), so
    # nothing can be cut off at an edge and nothing is stretched out of shape. (Because the box
    # is in whole CSS pixels, a formula can come out a few per cent smaller than its nominal
    # size, most for one-line formulae; Flare's own pictures have the same property.)
    $root = [regex]::Match($svgText, '<svg\b[^>]*>')
    $newRoot = $root.Value
    $newRoot = [regex]::Replace($newRoot, '\swidth="[^"]*"', " width=`"$pixelWidth`"")
    $newRoot = [regex]::Replace($newRoot, '\sheight="[^"]*"', " height=`"$pixelHeight`"")
    $sized = $svgText.Substring(0, $root.Index) + $newRoot + $svgText.Substring($root.Index + $root.Length)

    $svg = New-Object Svg.Skia.SKSvg
    $surface = $null; $image = $null; $pixels = $null; $data = $null
    try {
        $picture = $svg.FromSvg($sized)
        if ($null -eq $picture) { throw 'Svg.Skia could not read the drawing' }
        $cull = $picture.CullRect
        if ($cull.Width -le 0 -or $cull.Height -le 0) { throw 'the drawing is empty' }

        # 32 bits per pixel on a transparent background, as Flare's own pictures are.
        $info = New-Object SkiaSharp.SKImageInfo($pixelWidth, $pixelHeight, [SkiaSharp.SKColorType]::Rgba8888, [SkiaSharp.SKAlphaType]::Premul)
        $surface = [SkiaSharp.SKSurface]::Create($info)
        if ($null -eq $surface) { throw "could not make a $pixelWidth x $pixelHeight drawing surface" }
        $canvas = $surface.Canvas
        $canvas.Clear([SkiaSharp.SKColors]::Transparent)
        # The picture should already be exactly the size of the surface; scaling it to fit
        # costs nothing and keeps the result right if a future Svg.Skia sizes it differently.
        $canvas.Scale([single]($pixelWidth / $cull.Width), [single]($pixelHeight / $cull.Height))
        $canvas.Translate([single](-$cull.Left), [single](-$cull.Top))
        $canvas.DrawPicture($picture)
        $canvas.Flush()
        $image = $surface.Snapshot()
        # The pictures make the CHM bigger, so squeeze them: for black line art on a transparent
        # background, no row filter and the strongest compression gave the smallest files
        # (13% smaller in total than Skia's defaults on this help's 258 equations).
        $options = New-Object SkiaSharp.SKPngEncoderOptions([SkiaSharp.SKPngEncoderFilterFlags]::None, 9)
        $pixels = $image.PeekPixels()
        if ($null -eq $pixels) { throw 'could not read the drawn pixels' }
        $data = $pixels.Encode($options)
        if ($null -eq $data) { throw 'PNG encoding failed' }
        return ,$data.ToArray()   # the comma stops PowerShell unrolling the byte array
    }
    finally {
        if ($data) { $data.Dispose() }
        if ($pixels) { $pixels.Dispose() }
        if ($image) { $image.Dispose() }
        if ($surface) { $surface.Dispose() }
        $svg.Dispose()
    }
}


function Restore-File([string]$path, [byte[]]$original) {
    # Puts a file back as it was unless it still is. True if the file now has its original
    # content. (A file that was never changed, because it cannot be written, counts as restored.)
    try {
        $now = [IO.File]::ReadAllBytes($path)
        if ((Get-ContentHash $now) -ne (Get-ContentHash $original)) { [IO.File]::WriteAllBytes($path, $original) }
        return $true
    } catch { return $false }
}


function Invoke-HelpCompiler([string]$hhc, [string]$folder, [string]$hhpName) {
    # Runs hhc.exe and returns what it printed (Out) and what it sent to the error stream (Err,
    # nothing in practice). Its exit code is not used because it is no guide:
    # hhc's convention is 1 for success, yet both 1 and 0 have been seen on this PC for a good
    # CHM. The caller judges by what hhc printed and by the compiled file instead.
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $hhc
    $psi.Arguments = "`"$hhpName`""
    $psi.WorkingDirectory = $folder      # the .hhp names its files relative to its own folder
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($psi)
    # Read both streams while it runs; a full pipe would otherwise stall the compiler.
    $out = $process.StandardOutput.ReadToEndAsync()
    $err = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(15 * 60 * 1000)) {
        try { $process.Kill() } catch { }
        throw 'hhc.exe did not finish within 15 minutes'
    }
    $process.WaitForExit()
    Note "hhc.exe exit code $($process.ExitCode) (not used; its messages and the compiled file are checked instead)"
    return @{ Out = $out.Result; Err = $err.Result }
}


function Get-CompilerComplaints([string]$hhcOutput, [string]$hhcErrors) {
    # What hhc.exe said that means the CHM is not complete; nothing if all is well.
    # hhc writes a CHM even when it could not compile one of the files (a picture held open by
    # another program, say): it reports "HHC5003: Error: Compilation failed while compiling X"
    # and names X under "The following files were not compiled:". (On a PC where the search
    # component is not registered that heading is printed after every compile, with nothing under
    # it when all went well; otherwise it appears only when there is something to list.) HHC6003 is
    # set aside here: it says that the search component is not registered, Flare's own compile
    # gets it too, and what it costs is reported by Get-MissingIndexes below. Warnings (HHC3004
    # about the <?xml ...?> first line of every topic) are normal.
    $complaints = @()
    foreach ($m in [regex]::Matches("$hhcOutput`r`n$hhcErrors", '(?m)^\s*(HHC\d+): Error:[^\r\n]*')) {
        if ($m.Groups[1].Value -ne 'HHC6003') { $complaints += $m.Value.Trim() }
    }
    $heading = 'The following files were not compiled:'
    $at = $hhcOutput.LastIndexOf($heading)
    if ($at -ge 0) {
        $left = @($hhcOutput.Substring($at + $heading.Length) -split '[\r\n]+' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($left.Count -gt 0) { $complaints += "not compiled: $($left -join ', ')" }
    }
    return $complaints
}


function Get-MissingIndexes([string]$chmPath) {
    # Which of the two indexes a CHM lacks: the full-text index behind the Search tab and the
    # keyword index. The help compiler leaves both out, and still reports success, on a PC where
    # Flare's itcc.dll is not registered (HHC6003; Flare's Resources\Bin\RegisterItcc.bat, run as
    # administrator, registers it). A CHM lists the names of the things inside it in plain text,
    # so looking for the names is enough.
    $text = $script:Latin1.GetString([IO.File]::ReadAllBytes($chmPath))
    $missing = @()
    if (-not $text.Contains('$FIftiMain')) { $missing += 'the full-text search index' }
    if (-not $text.Contains('$WWKeywordLinks')) { $missing += 'the keyword index' }
    return $missing
}


function Invoke-Sharpen {
    if (-not $OutputDirectory) { throw 'no -OutputDirectory given' }
    if ($OutputDirectory.Contains('"')) { throw 'the -OutputDirectory value has swallowed its closing quote; write "$(OutputDirectory)." with the full stop (see the top of the script)' }
    $number = 0.0; $whole = 0
    if (-not [double]::TryParse($PixelsPerEx, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or $number -lt 1 -or $number -gt 64) { throw "-PixelsPerEx '$PixelsPerEx' is not a number from 1 to 64" }
    if (-not [int]::TryParse($Density, [ref]$whole) -or $whole -lt 1 -or $whole -gt 8) { throw "-Density '$Density' is not a whole number from 1 to 8" }
    $PixelsPerEx = $number; $Density = $whole     # from here on they are numbers
    if ($script:Unknown) { Say "WARNING: arguments not understood and ignored: $($script:Unknown -join ' ')" }

    # ---- 1. Where things are ------------------------------------------------------------------
    # GetFullPath tidies the "\." that the post-build command line adds (see the top of this file).
    $outDir = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
    $target = $TargetName
    if (-not $target) { $target = Split-Path $outDir -Leaf }   # Flare names the output folder after the target
    $tempTarget = Join-Path (Split-Path $outDir -Parent) "Temporary\$target"
    $content = Join-Path $tempTarget 'Content'
    $hhp = Join-Path $content '_Temp.hhp'
    if (-not (Test-Path -LiteralPath $hhp)) {
        # Some other kind of target (HTML5, PDF...): nothing for us to do, and nothing wrong.
        Say "no HTML Help project at $hhp; nothing to do."
        return
    }
    $script:LogFile = Join-Path $tempTarget 'sharpen-chm-equations.log'
    Note "Run of $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  PixelsPerEx=$PixelsPerEx  Density=$Density"
    Note "Output folder : $outDir"
    Note "Content folder: $content"
    # While pictures and topics are being written a marker file exists beside the log (step 5).
    # Found at the start, it means an earlier run was cut short in the middle of that, or failed
    # and could not put things back, so pictures and topics may no longer belong together.
    $marker = Join-Path $tempTarget 'sharpen-chm-equations.writing'
    if (Test-Path -LiteralPath $marker) { throw "an earlier run broke off while writing pictures and topics, which may no longer belong together. Build the target again in Flare (that empties the folder; should this message come back after a fresh build, delete $marker)" }
    if ($Density -gt 2) { Say "WARNING: -Density $Density. On a display at 100% scaling the help viewer shrinks such pictures $Density to 1 and loses thin strokes (an '=' can read as '-'). 2 is the safe value; see the top of the script." }

    # The name of the compiled file is whatever the .hhp says, not something we assume.
    $hhpText = [IO.File]::ReadAllText($hhp, [Text.Encoding]::Default)
    $m = [regex]::Match($hhpText, '(?im)^\s*Compiled file\s*=\s*(.+?)\s*$')
    if (-not $m.Success) { throw "no 'Compiled file=' line in $hhp" }
    $chmName = Split-Path $m.Groups[1].Value -Leaf
    $builtChm = Join-Path $outDir $chmName
    if (-not (Test-Path -LiteralPath $builtChm)) { throw "Flare's $chmName is not in $outDir" }

    $eqFolder = Join-Path $content 'GeneratedImages\Equations'
    $pictures = @()
    if (Test-Path -LiteralPath $eqFolder) { $pictures = @(Get-ChildItem -LiteralPath $eqFolder -Filter *.png -File) }
    if ($pictures.Count -eq 0) { Say 'this help has no equation pictures; nothing to do.'; return }

    # ---- 2. What size does each topic show each picture at? -------------------------------------
    # Flare always writes the picture's own pixel size into the <img> style. A picture whose
    # pixel size differs from its styled size has therefore been redrawn already (by an earlier
    # run of this script on the same build) and must be left alone.
    $topics = @{}        # path -> text, only for topics that show equations
    $styled = @{}        # picture file name -> styled size
    foreach ($file in Get-ChildItem -LiteralPath $content -Recurse -File) {
        if ($file.Extension -notmatch '^\.html?$') { continue }
        $text = $script:Latin1.GetString([IO.File]::ReadAllBytes($file.FullName))
        $tags = [regex]::Matches($text, $script:ImgPattern)
        if ($tags.Count -eq 0) { continue }
        $topics[$file.FullName] = $text
        foreach ($tag in $tags) {
            $size = Get-StyledSize $tag.Value
            if ($size) { $styled[$tag.Groups[1].Value] = $size }
        }
    }
    Note "$($topics.Count) topics show equations."

    # ---- 3. Match every picture to its vector drawing in Flare's cache --------------------------
    $candidates = @{}    # picture file name -> bytes, for pictures still as Flare drew them
    $alreadyDone = 0; $notShown = 0
    foreach ($png in $pictures) {
        $bytes = [IO.File]::ReadAllBytes($png.FullName)
        $size = Get-PngSize $bytes
        $shown = $styled[$png.Name]
        if (-not $size -or -not $shown -or $shown.Width -lt 1 -or $shown.Height -lt 1) { $notShown++; Note "left alone (no topic shows it with a size, or not a PNG): $($png.Name)"; continue }
        if ($size.Width -ne $shown.Width -or $size.Height -ne $shown.Height) { $alreadyDone++; continue }
        $candidates[$png.Name] = $bytes
    }

    # Has an earlier run on this build finished its work? It has if the CHM in the Content folder
    # was compiled after the pictures were redrawn and the CHM in the output folder is a copy of it.
    # If not, that run redrew the pictures but failed at the compile or the copy (a locked file,
    # say), and this run must compile and copy even if it finds no picture left to redraw.
    $compiled = Join-Path $content $chmName
    $finished = $false
    if ($alreadyDone -gt 0) {
        $newestPicture = ($pictures | Measure-Object LastWriteTimeUtc -Maximum).Maximum
        $finished = (Test-Path -LiteralPath $compiled) -and
                    ((Get-Item -LiteralPath $compiled).LastWriteTimeUtc -ge $newestPicture) -and
                    ((Get-ContentHash ([IO.File]::ReadAllBytes($compiled))) -eq (Get-ContentHash ([IO.File]::ReadAllBytes($builtChm))))
    }
    if ($candidates.Count -eq 0) {
        if ($alreadyDone -eq 0) { throw "none of the $($pictures.Count) equation pictures is shown by a topic at its own size" }
        if ($finished) {
            Say "nothing to do: all $alreadyDone equation pictures of this build have been redrawn already. Pictures, topics and $chmName left as they are."
            return
        }
        Note "All $alreadyDone pictures were redrawn by an earlier run, but $chmName in the output folder is not the recompiled one; compiling again."
    }

    $cacheRoot = $CacheFolder
    if (-not $cacheRoot) { $cacheRoot = Join-Path ([IO.Path]::GetTempPath()) 'MadCap Software\Flare\Equations' }
    Note "Equation cache: $cacheRoot"
    # Flare keeps the cache in a version folder (V3 today). Look in all of them, and in the folder
    # itself so that -CacheFolder may point straight at a folder of .png/.svg pairs.
    $cacheFolders = @()
    if (Test-Path -LiteralPath $cacheRoot) {
        $cacheFolders = @($cacheRoot) + @(Get-ChildItem -LiteralPath $cacheRoot -Directory -Filter 'V*' | ForEach-Object { $_.FullName })
    }
    # Only cached pictures of the same length as one of ours can be identical to it; hashing just
    # those keeps this quick however large the cache has grown.
    $lengths = @{}
    foreach ($bytes in $candidates.Values) { $lengths[$bytes.Length] = $true }
    $drawingOf = @{}     # content hash of a cached PNG -> path of the SVG beside it
    foreach ($folder in $cacheFolders) {
        foreach ($cached in Get-ChildItem -LiteralPath $folder -Filter *.png -File) {
            if (-not $lengths.ContainsKey([int]$cached.Length)) { continue }
            $svgPath = [IO.Path]::ChangeExtension($cached.FullName, '.svg')
            if (-not (Test-Path -LiteralPath $svgPath)) { continue }
            $hash = Get-ContentHash ([IO.File]::ReadAllBytes($cached.FullName))
            # The same formula cached for several font sizes gives the same PNG and the same
            # drawing, so whichever comes first will do.
            if (-not $drawingOf.ContainsKey($hash)) { $drawingOf[$hash] = $svgPath }
        }
    }

    $matched = @{}       # picture file name -> SVG path
    $unmatched = @()
    foreach ($name in $candidates.Keys) {
        $svgPath = $drawingOf[(Get-ContentHash $candidates[$name])]
        if ($svgPath) { $matched[$name] = $svgPath } else { $unmatched += $name }
    }
    if ($matched.Count -eq 0 -and $candidates.Count -gt 0) {
        $noMatch = "none of the $($candidates.Count) equation pictures still as Flare drew them has a byte-identical copy with a drawing in Flare's equation cache ($cacheRoot)"
        if ($alreadyDone -eq 0) { throw $noMatch }
        if ($finished) {
            # Not a failure: the CHM in the output folder has the pictures an earlier run redrew.
            Say "WARNING: nothing more could be done: $alreadyDone equation pictures were redrawn by an earlier run; $noMatch. Pictures, topics and $chmName left as they are."
            Note "still as Flare drew them: $(($unmatched | Sort-Object) -join ', ')"
            return
        }
        Note "$noMatch; an earlier run redrew $alreadyDone pictures but did not finish, so compiling again."
    }

    # ---- 4. Redraw, in memory first --------------------------------------------------------------
    # Nothing on disk is changed until every picture has been drawn, so that a failure here
    # (Flare's libraries will not load, say) leaves the build exactly as Flare made it.
    # Flare and its drawing library are 64-bit; in 32-bit PowerShell the library fails to load with
    # a message that does not say why, and "Program Files" is not the folder Flare is in.
    if (-not [Environment]::Is64BitProcess) { throw 'this is 32-bit PowerShell, and Flare''s drawing library is 64-bit; use the ordinary (64-bit) powershell.exe' }
    $app = $FlareApp
    if (-not $app) { $app = Find-FlareApp }
    if (-not $app) { throw 'no MadCap Flare installation found; give -FlareApp' }
    $hhc = Join-Path $app 'Resources\Bin\hhc.exe'
    foreach ($needed in @((Join-Path $app 'Svg.Skia.dll'), (Join-Path $app 'SkiaSharp.dll'), (Join-Path $app 'libSkiaSharp.dll'), $hhc)) {
        if (-not (Test-Path -LiteralPath $needed)) { throw "not found: $needed" }
    }
    Note "Flare program folder: $app"
    Initialize-Skia $app

    $newPng = @{}        # picture file name -> bytes of the redrawn PNG
    $newSize = @{}       # picture file name -> new CSS size
    $failed = @()
    foreach ($name in ($matched.Keys | Sort-Object)) {
        try {
            $svgText = [IO.File]::ReadAllText($matched[$name])
            $ex = Get-SvgSizeInEx $svgText
            $cssW = [Math]::Max(1, [int][Math]::Round($ex.Width * $PixelsPerEx, [MidpointRounding]::AwayFromZero))
            $cssH = [Math]::Max(1, [int][Math]::Round($ex.Height * $PixelsPerEx, [MidpointRounding]::AwayFromZero))
            $bytes = Convert-SvgToPng $svgText ($cssW * $Density) ($cssH * $Density)
            # Believe the file, not the arithmetic: check the PNG really has the size we asked for.
            $made = Get-PngSize $bytes
            if (-not $made -or $made.Width -ne $cssW * $Density -or $made.Height -ne $cssH * $Density) { throw 'the redrawn picture has the wrong size' }

            # ...and that it holds the formula. Nothing else would notice a drawing that came out
            # blank or with symbols missing (a future Svg.Skia that cannot follow the drawing's
            # references to its symbol shapes, say). Ink grows with the square of the magnification,
            # and the magnification is how much bigger the drawing is fitted: the smaller of the
            # two ratios of box to drawing, now against then.
            $flare = Get-PngSize $candidates[$name]
            $fitThen = [Math]::Min($flare.Width / $ex.Width, $flare.Height / $ex.Height)
            $fitNow = [Math]::Min($made.Width / $ex.Width, $made.Height / $ex.Height)
            $inkThen = Get-Ink $candidates[$name]
            if ($inkThen -le 0) { throw 'Flare''s own picture is blank' }
            $inkRatio = (Get-Ink $bytes) / ($inkThen * [Math]::Pow($fitNow / $fitThen, 2))
            if ($inkRatio -lt $script:InkLowest -or $inkRatio -gt $script:InkHighest) { throw ("the redrawn picture holds {0:0.00} times the ink expected from Flare's picture" -f $inkRatio) }

            $newPng[$name] = $bytes
            $newSize[$name] = @{ Width = $cssW; Height = $cssH }
            Note ("{0}: {1} x {2} ex; was {3} x {4}; now {5} x {6} CSS px, {7} x {8} dots; ink {9:0.000} of expected; {10}" -f $name, $ex.Width, $ex.Height, $styled[$name].Width, $styled[$name].Height, $cssW, $cssH, $made.Width, $made.Height, $inkRatio, (Split-Path $matched[$name] -Leaf))
        } catch {
            $failed += $name
            Note "could not redraw ${name}: $(Get-Reason $_)"
        }
    }
    if ($newPng.Count -eq 0 -and $matched.Count -gt 0) { throw "none of the $($matched.Count) matched drawings could be redrawn (see $($script:LogFile))" }

    # New topic texts: only the width and height inside the style of a redrawn picture's <img>.
    $newTopics = @{}
    $script:TagsChanged = 0
    $evaluator = [Text.RegularExpressions.MatchEvaluator] {
        param($tag)
        $size = $newSize[$tag.Groups[1].Value]
        $style = [regex]::Match($tag.Value, '(?i)\bstyle="[^"]*"')
        if (-not $size -or -not $style.Success) { return $tag.Value }
        $s = [regex]::Replace($style.Value, '(?i)(?<![-\w])(width:\s*)\d+px', ('${1}' + $size.Width + 'px'))
        $s = [regex]::Replace($s, '(?i)(?<![-\w])(height:\s*)\d+px', ('${1}' + $size.Height + 'px'))
        $script:TagsChanged++
        return $tag.Value.Substring(0, $style.Index) + $s + $tag.Value.Substring($style.Index + $style.Length)
    }
    foreach ($path in $topics.Keys) {
        $changed = [regex]::Replace($topics[$path], $script:ImgPattern, $evaluator)
        if (-not [string]::Equals($changed, $topics[$path])) { $newTopics[$path] = $changed }
    }

    # ---- 5. Write pictures and topics, compile ---------------------------------------------------
    # Pictures and topics belong together: new pictures in topics that still give the old sizes
    # would be shown at the wrong size. So if any file cannot be written (a topic held open or
    # marked read-only, a full disc), put back every file already written; the originals are
    # still in memory. The marker file says "writing in progress" to any later run, in case this
    # one is cut short (the PC switched off, say) and never reaches the lines that put things back.
    $pictureBytes = 0; $pictureBytesBefore = 0
    [IO.File]::WriteAllText($marker, "Pictures and topics were being written from $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    try {
        foreach ($name in $newPng.Keys) {
            [IO.File]::WriteAllBytes((Join-Path $eqFolder $name), $newPng[$name])
            $pictureBytes += $newPng[$name].Length; $pictureBytesBefore += $candidates[$name].Length
        }
        foreach ($path in $newTopics.Keys) { [IO.File]::WriteAllBytes($path, $script:Latin1.GetBytes($newTopics[$path])) }
    } catch {
        $why = Get-Reason $_
        $stuck = @()
        foreach ($name in $newPng.Keys) { if (-not (Restore-File (Join-Path $eqFolder $name) $candidates[$name])) { $stuck += $name } }
        foreach ($path in $newTopics.Keys) { if (-not (Restore-File $path ($script:Latin1.GetBytes($topics[$path])))) { $stuck += $path } }
        if ($stuck) {
            Note "could not be put back: $($stuck -join ', ')"
            throw "$why AND $($stuck.Count) files of the Content folder could not be put back as they were. Build the target again in Flare"
        }
        [IO.File]::Delete($marker)
        throw "$why (the pictures and topics already written have been put back as they were)"
    }
    [IO.File]::Delete($marker)
    Note "$($newPng.Count) pictures and $($newTopics.Count) topics written ($($script:TagsChanged) <img> tags)."

    $started = [DateTime]::UtcNow
    $said = Invoke-HelpCompiler $hhc $content (Split-Path $hhp -Leaf)
    Note '---- hhc.exe said:'
    Note $said.Out.TrimEnd()
    if ($said.Err.Trim()) { Note '---- and on its error stream:'; Note $said.Err.TrimEnd() }
    Note '----'

    # Is the new CHM good? Three tests. hhc must not have reported a file it could not compile
    # (it writes a CHM regardless, with that file missing). The CHM must be newly written. And it
    # must be about as big as expected: the CHM that was there plus what the pictures have grown
    # by, since PNGs hardly compress further inside a CHM (this help came to 99% of that sum).
    $complaints = @(Get-CompilerComplaints $said.Out $said.Err)
    if ($complaints.Count -gt 0) { throw "the help compiler did not compile everything: $($complaints -join '; ')" }
    if (-not (Test-Path -LiteralPath $compiled)) { throw "hhc.exe did not produce $compiled" }
    $new = Get-Item -LiteralPath $compiled
    $flareLength = (Get-Item -LiteralPath $builtChm).Length
    $expected = $flareLength + $pictureBytes - $pictureBytesBefore
    Note ("new {0}: {1:N0} bytes; expected about {2:N0} ({3:N0} there before, pictures grown by {4:N0})" -f $chmName, $new.Length, $expected, $flareLength, ($pictureBytes - $pictureBytesBefore))
    if ($new.LastWriteTimeUtc -lt $started.AddSeconds(-2)) { throw "hhc.exe did not write a new $chmName" }
    if ($new.Length -lt 0.95 * $expected) { throw ("the recompiled $chmName is implausibly small: {0:N0} bytes, where about {1:N0} were expected" -f $new.Length, $expected) }
    # The recompiled CHM must not have lost an index that Flare's own compile produced. If
    # Flare's has none either, the CHM is as good as Flare made it, so carry on, but say so:
    # nothing else in the build would.
    $lacking = @(Get-MissingIndexes $compiled)
    $flareLacked = @(Get-MissingIndexes $builtChm)
    $lost = @($lacking | Where-Object { $flareLacked -notcontains $_ })
    if ($lost.Count -gt 0) { throw "the recompiled $chmName lacks $($lost -join ' and '), which Flare's own has" }
    if ($lacking.Count -gt 0) { Say "WARNING: $chmName has no $($lacking -replace '^the ', '' -join ' and no ') (Flare's own compile has none either): the help compiler's itcc.dll is not registered on this PC. Run Flare's Resources\Bin\RegisterItcc.bat as administrator and build again." }

    # ---- 6. Put the new CHM where Flare publishes from ------------------------------------------
    # First keep Flare's own CHM beside our log. If the file is there already it is from an
    # earlier run on this build (Flare empties the folder at every build) and is the one Flare
    # made, so it is left alone. A copy inherits "read-only" from its original; clear that, or
    # the kept file could never be replaced.
    $backup = Join-Path $tempTarget "$chmName.as-flare-built"
    if (-not (Test-Path -LiteralPath $backup)) {
        [IO.File]::Copy($builtChm, $backup)
        [IO.File]::SetAttributes($backup, [IO.FileAttributes]::Normal)
    }
    $before = [IO.File]::ReadAllBytes($builtChm)
    try {
        [IO.File]::Copy($compiled, $builtChm, $true)
        if ((Get-Item -LiteralPath $builtChm).Length -ne $new.Length) { throw 'the copy is incomplete' }
    } catch {
        $why = Get-Reason $_
        try { if ((Get-Item -LiteralPath $builtChm).IsReadOnly) { $why += ' (the file is marked read-only)' } } catch { }
        # A copy that Windows refused (the CHM is open in the help viewer, or read-only) has
        # changed nothing. Only a copy that broke off part-way has to be undone. This is kept
        # apart so that a failure here cannot hide the reason for the first failure.
        try {
            if ((Get-ContentHash ([IO.File]::ReadAllBytes($builtChm))) -ne (Get-ContentHash $before)) { [IO.File]::WriteAllBytes($builtChm, $before) }
        } catch {
            $why += "; AND the $chmName that was there could not be checked or put back ($(Get-Reason $_)). Flare's own is kept as $backup"
        }
        throw "could not copy the recompiled $chmName to ${outDir}: $why"
    }

    Say ("{0} of {1} equation pictures redrawn at {2} px per ex with {3}x the dots; {4} topics updated; {5} recompiled: {6:N0} bytes (was {7:N0})." -f $newPng.Count, $pictures.Count, $PixelsPerEx, $Density, $newTopics.Count, $chmName, $new.Length, $flareLength)
    if ($alreadyDone) { Say "$alreadyDone pictures had been redrawn already and were left alone." }
    if ($notShown)    { Say "WARNING: $notShown pictures are not shown with a size by any topic and were left alone." }
    if ($unmatched) {
        Say "WARNING: $($unmatched.Count) pictures have no byte-identical copy in Flare's equation cache and were left as Flare drew them: $(Join-Names $unmatched)"
        Note "all of them: $(($unmatched | Sort-Object) -join ', ')"
    }
    if ($failed) {
        Say "WARNING: $($failed.Count) pictures could not be redrawn and were left as Flare drew them: $(Join-Names $failed)"
        Note "all of them: $(($failed | Sort-Object) -join ', ')"
    }
}


try {
    Invoke-Sharpen
}
catch {
    # One line, starting with words that are easy to search the build log for.
    Write-Host "EQUATIONS NOT SHARPENED: $(Get-Reason $_)"
    Note "EQUATIONS NOT SHARPENED: $(Get-Reason $_)"
    Note $_.ScriptStackTrace
}
finally {
    if ($script:LogFile) {
        # Added to, not replaced: a later run that finds nothing to do must not wipe the record
        # of the run that did the work. Flare empties the folder at every build, so it cannot
        # grow without end.
        try { [IO.File]::AppendAllLines($script:LogFile, [string[]](@('', ('=' * 78)) + $script:LogLines)) } catch { Write-Host "sharpen-chm-equations: could not write $($script:LogFile)" }
    }
}
# Always 0: a help file with soft equations is better than a build that stops.
exit 0
