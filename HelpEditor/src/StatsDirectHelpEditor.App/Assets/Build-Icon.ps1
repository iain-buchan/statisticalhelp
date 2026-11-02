[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$family=[Drawing.FontFamily]::new('Times New Roman')
$outline=[Drawing.Drawing2D.GraphicsPath]::new()
try {
    $outline.AddString([string][char]0x03B4,$family,0,100,[Drawing.PointF]::new(0,0),[Drawing.StringFormat]::GenericTypographic)
    $bounds=$outline.GetBounds()
    $scale=77/$bounds.Height
    $culture=[Globalization.CultureInfo]::InvariantCulture
    function N([single]$value){$value.ToString('0.###',$culture)}
    function P($point){(N $point.X)+' '+(N $point.Y)}
    $path=[Text.StringBuilder]::new()
    $points=$outline.PathPoints;$types=$outline.PathTypes
    for($i=0;$i -lt $points.Length;$i++){
        switch($types[$i] -band 7){
            0 {[void]$path.Append('M'+(P $points[$i]))}
            1 {[void]$path.Append('L'+(P $points[$i]))}
            3 {[void]$path.Append('C'+(P $points[$i])+' '+(P $points[$i+1])+' '+(P $points[$i+2]));$i+=2}
            default {throw 'Unexpected glyph outline segment'}
        }
        if($types[$i] -band 128){[void]$path.Append('Z')}
    }
    # Outline the Greek letter once so rendering has no runtime font dependency.
    $transform='translate(12 6) scale('+(N $scale)+') translate('+(N (-$bounds.X))+' '+(N (-$bounds.Y))+')'
    $svg=@"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 96 96">
  <title>StatsDirect Help Editor</title>
  <desc>The turquoise StatsDirect delta tile with an open book and gold editing pencil.</desc>
  <defs><linearGradient id="tile" x2="0" y2="1"><stop stop-color="#acf2f7"/><stop offset="1" stop-color="#94e4ef"/></linearGradient></defs>
  <rect x="1.5" y="1.5" width="93" height="93" rx="3" fill="url(#tile)" stroke="#50bdc5" stroke-width="3"/>
  <path d="M4 91V4H91" fill="none" stroke="#dcfbff" stroke-width="1.3"/>
  <path fill="#10366f" fill-rule="evenodd" transform="$transform" d="$path"/>
  <path d="M54 60Q64 55 73 60Q82 55 92 60V86Q83 82 73 88Q64 82 54 86Z" fill="#fcfeff" stroke="#204f69" stroke-width="2.6" stroke-linejoin="round"/>
  <path d="M73 61V87M59 65Q64 63 69 66M59 71Q64 69 69 72M78 65Q83 63 88 65" fill="none" stroke="#81b2c1" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M64 84L86 58L91 62L69 88Z" fill="#f5c24d" stroke="#8b632b" stroke-width="1.4" stroke-linejoin="round"/>
  <path d="M86 58L89 54Q90 53 91 54L95 57Q96 58 95 59L91 63Z" fill="#e2948b" stroke="#8b632b" stroke-width="1.3" stroke-linejoin="round"/>
  <path d="M64 84L69 88L60 92Z" fill="#ffe7b8" stroke="#8b632b" stroke-width="1.3" stroke-linejoin="round"/>
  <path d="M60 92L62 87L65 90Z" fill="#233e55"/>
  <path d="M67 83L87 60" stroke="#ffe6a0" stroke-width="1.5"/>
</svg>
"@
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'HelpEditor.svg'),$svg,[Text.UTF8Encoding]::new($false))
} finally {$outline.Dispose();$family.Dispose()}
& node (Join-Path $PSScriptRoot 'render-icon.cjs')
if($LASTEXITCODE -ne 0){throw 'Icon rendering failed'}
