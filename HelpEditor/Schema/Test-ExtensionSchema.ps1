[CmdletBinding()]
param(
    [string]$SourceProject = (Join-Path (Split-Path -Parent $PSScriptRoot) 'HelpSource'),
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Xml.Linq
$canonical = 'https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd'
$legacy = 'http://www.madcapsoftware.com/Schemas/MadCap.xsd'
$schemaPath = Join-Path $PSScriptRoot 'StatsDirectHelp.xsd'
$schemas = [Xml.Schema.XmlSchemaSet]::new()
$schemas.XmlResolver = $null
[void]$schemas.Add($canonical, $schemaPath)
$schemas.Compile()
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed, [object]$Detail = $null) {
    $checks.Add([pscustomobject]@{name=$Name;passed=$Passed;detail=$Detail})
}
function ValidateExtension($Node) {
    $errors = [Collections.Generic.List[string]]::new()
    $handler = [Xml.Schema.ValidationEventHandler]{ param($sender, $eventArgs)
        if ($eventArgs.Severity -eq [Xml.Schema.XmlSeverityType]::Error) { $errors.Add($eventArgs.Message) }
    }
    $name = [Xml.XmlQualifiedName]::new($Node.Name.LocalName, $canonical)
    $table = if ($Node -is [Xml.Linq.XAttribute]) { $schemas.GlobalAttributes } else { $schemas.GlobalElements }
    $declaration = $table[$name]
    if (-not $declaration) { $errors.Add('Undeclared extension: ' + $Node.Name.ToString()) }
    else { [Xml.Schema.Extensions]::Validate($Node, $declaration, $schemas, $handler) }
    return $errors.ToArray()
}
function NormalizeInMemory([Xml.Linq.XDocument]$Document) {
    foreach ($element in $Document.Descendants()) {
        if ($element.Name.NamespaceName -eq $legacy) { $element.Name = [Xml.Linq.XName]::Get($element.Name.LocalName, $canonical) }
        foreach ($attribute in @($element.Attributes())) {
            if ($attribute.IsNamespaceDeclaration -and $attribute.Value -eq $legacy) { $attribute.Value = $canonical }
            elseif ($attribute.Name.NamespaceName -eq $legacy) {
                $element.SetAttributeValue([Xml.Linq.XName]::Get($attribute.Name.LocalName, $canonical), $attribute.Value)
                $attribute.Remove()
            }
        }
    }
}
$cases = @(
    @{name='Plain TeX equation';xml='<sdh:equation>$x^2$</sdh:equation>';valid=$true},
    @{name='MathML equation';xml='<sdh:equation><math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mi>x</mi><mn>2</mn></mfrac></math></sdh:equation>';valid=$true},
    @{name='Mixed HTML, XHTML, SVG and text';xml='<sdh:pageFooter>Text <p>HTML</p><p xmlns="http://www.w3.org/1999/xhtml">XHTML</p><svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0L1 1"/></svg></sdh:pageFooter>';valid=$true},
    @{name='R dropdown with image heading';xml='<sdh:dropDown class="rcode"><sdh:dropDownHead><sdh:dropDownHotspot><img src="r.png"/> R code</sdh:dropDownHotspot></sdh:dropDownHead><sdh:dropDownBody><pre>mean(c(1, 2, 3))</pre></sdh:dropDownBody></sdh:dropDown>';valid=$true},
    @{name='Canonical typed authoring attributes';xml='<sdh:bodyProxy sdh:conditions="Primary.SWHELP" sdh:searchable="false" sdh:lastBlockDepth="0" sdh:lastHeight="12.5" sdh:lastWidth="0"/>';valid=$true},
    @{name='Global authoring attribute on ordinary HTML';xml='<p sdh:searchable="false">HTML content</p>';valid=$true},
    @{name='Retained legacy boolean and metadata typo';xml='<sdh:equation sdh:searchable="False" sdh:lastBlocdatackDepth="2">x</sdh:equation>';valid=$true},
    @{name='Keyword term';xml='<sdh:keyword term="Example;R example"/>';valid=$true},
    @{name='Invalid searchable boolean rejected';xml='<sdh:equation sdh:searchable="perhaps"/>';valid=$false},
    @{name='Negative dimension rejected';xml='<sdh:equation sdh:lastHeight="-1"/>';valid=$false},
    @{name='Nonnumeric dimension rejected';xml='<sdh:equation sdh:lastWidth="wide"/>';valid=$false},
    @{name='Fractional depth rejected';xml='<sdh:equation sdh:lastBlockDepth="1.5"/>';valid=$false},
    @{name='Keyword missing term rejected';xml='<sdh:keyword/>';valid=$false},
    @{name='Keyword empty term rejected';xml='<sdh:keyword term=""/>';valid=$false},
    @{name='Dropdown missing body rejected';xml='<sdh:dropDown><sdh:dropDownHead><sdh:dropDownHotspot>R</sdh:dropDownHotspot></sdh:dropDownHead></sdh:dropDown>';valid=$false},
    @{name='Dropdown incorrect order rejected';xml='<sdh:dropDown><sdh:dropDownBody/><sdh:dropDownHead><sdh:dropDownHotspot>R</sdh:dropDownHotspot></sdh:dropDownHead></sdh:dropDown>';valid=$false},
    @{name='Body insertion placeholder content rejected';xml='<sdh:bodyProxy><p>Unexpected</p></sdh:bodyProxy>';valid=$false},
    @{name='Unknown extension element rejected';xml='<sdh:unknown/>';valid=$false},
    @{name='Unknown extension attribute rejected';xml='<sdh:equation sdh:serchable="false"/>';valid=$false},
    @{name='Invalid typed attribute on ordinary HTML rejected';xml='<p sdh:lastHeight="wide">HTML content</p>';valid=$false},
    @{name='Unknown extension nested inside ordinary HTML rejected';xml='<sdh:pageFooter><p><sdh:unknown/></p></sdh:pageFooter>';valid=$false}
)
foreach ($case in $cases) {
    $fixture = [Xml.Linq.XDocument]::Parse('<fixture xmlns:sdh="' + $canonical + '">' + $case.xml + '</fixture>')
    $errors = @(foreach ($element in $fixture.Descendants()) {
        if ($element.Name.NamespaceName -eq $canonical) { ValidateExtension $element }
        foreach ($attribute in $element.Attributes() | Where-Object { $_.Name.NamespaceName -eq $canonical }) { ValidateExtension $attribute }
    })
    Check $case.name (($errors.Count -eq 0) -eq $case.valid) $errors
}
# Exercise legacy import without changing any real page or relying on its prefix.
$legacyFixture = [Xml.Linq.XDocument]::Parse('<other:equation xmlns:other="' + $legacy + '" other:searchable="False">x</other:equation>')
NormalizeInMemory $legacyFixture
$legacyErrors = @(ValidateExtension $legacyFixture.Root)
Check 'Legacy namespace maps in memory and retains boolean content' ($legacyErrors.Count -eq 0 -and $legacyFixture.Root.Name.NamespaceName -eq $canonical) $legacyErrors

$SourceProject = [IO.Path]::GetFullPath($SourceProject)
$xmlTypes = @('.htm','.html','.xhtml','.fltoc','.fltar','.flvar','.flcts','.flmsp','.flskn','.flpal','.flsnp','.flprj','.flali','.flbrs','.fldes','.flglo','.fluser','.mcdic','.props','.xml','.svg')
$files = @(Get-ChildItem -LiteralPath $SourceProject -File -Recurse -Force)
$roots = @{}; $elements = @{}; $attributes = @{}; $namespaces = @{}; $sourceErrors = [Collections.Generic.List[object]]::new()
$validatedElements = 0; $validatedAttributes = 0; $parsedFiles = 0
foreach ($file in $files | Where-Object Extension -in $xmlTypes) {
    $relative = $file.FullName.Substring($SourceProject.TrimEnd('\').Length + 1)
    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.XmlResolver = $null
        # Some SVG imports declare a public DTD. Do not fetch it or expand entities.
        $settings.DtdProcessing = [Xml.DtdProcessing]::Ignore
        $reader = [Xml.XmlReader]::Create($file.FullName, $settings)
        try { $document = [Xml.Linq.XDocument]::Load($reader) } finally { $reader.Dispose() }
        $parsedFiles++
        $rootName = $file.Extension + '|' + $document.Root.Name.ToString()
        if (-not $roots.ContainsKey($rootName)) { $roots[$rootName] = 0 }; $roots[$rootName]++
        foreach ($element in $document.Descendants()) {
            if ($element.Name.NamespaceName -in @($legacy, $canonical)) {
                $name = $element.Name.LocalName
                if (-not $elements.ContainsKey($name)) { $elements[$name] = 0 }; $elements[$name]++
                if (-not $namespaces.ContainsKey($element.Name.NamespaceName)) { $namespaces[$element.Name.NamespaceName] = 0 }; $namespaces[$element.Name.NamespaceName]++
            }
            foreach ($attribute in $element.Attributes() | Where-Object { $_.Name.NamespaceName -in @($legacy, $canonical) }) {
                $name = $attribute.Name.LocalName
                if (-not $attributes.ContainsKey($name)) { $attributes[$name] = @{count=0;values=@{}} }
                $attributes[$name].count++; $attributes[$name].values[$attribute.Value] = $true
            }
        }
        NormalizeInMemory $document
        foreach ($element in $document.Descendants()) {
            if ($element.Name.NamespaceName -eq $canonical) {
                $validatedElements++
                foreach ($errorText in @(ValidateExtension $element)) { $sourceErrors.Add([pscustomobject]@{path=$relative;node=$element.Name.LocalName;error=$errorText}) }
            }
            foreach ($attribute in $element.Attributes() | Where-Object { $_.Name.NamespaceName -eq $canonical }) {
                $validatedAttributes++
                foreach ($errorText in @(ValidateExtension $attribute)) { $sourceErrors.Add([pscustomobject]@{path=$relative;attribute=$attribute.Name.LocalName;error=$errorText}) }
            }
        }
    } catch { $sourceErrors.Add([pscustomobject]@{path=$relative;error=$_.Exception.Message}) }
}
Check 'All inventoried source extensions and authoring attributes validate' ($sourceErrors.Count -eq 0) @($sourceErrors)
$attributeInventory = @($attributes.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{name=$_;count=$attributes[$_].count;distinctValues=$attributes[$_].values.Count;sampleValues=@($attributes[$_].values.Keys | Sort-Object | Select-Object -First 5)} })
$report = [ordered]@{
    checkedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');schema=$schemaPath;sourceProject=$SourceProject
    sourceModified=$false;externalXmlResolution=$false;parsedXmlFiles=$parsedFiles;sourceFileCount=$files.Count
    sourceFileTypes=@($files | Group-Object Extension | Sort-Object Name | ForEach-Object { [pscustomobject]@{extension=$_.Name;count=$_.Count} })
    roots=$roots;extensionNamespaces=$namespaces;extensionElements=$elements;extensionAttributes=$attributeInventory
    validatedElements=$validatedElements;validatedAttributes=$validatedAttributes
    passed=@($checks | Where-Object passed).Count;failed=@($checks | Where-Object { -not $_.passed }).Count;checks=@($checks)
}
$json = $report | ConvertTo-Json -Depth 10
if ($OutputPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
$json
if ($report.failed) { throw 'StatsDirect extension schema checks failed. See the report.' }
