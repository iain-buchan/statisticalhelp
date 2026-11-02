# StatsDirect Help source schema

`StatsDirectHelp.xsd` defines the authoring extensions that the independent
StatsDirect Help Editor uses. It is original project code under the repository's
MIT licence. It neither includes nor downloads MadCap's proprietary schema and
does not require a Flare licence or installation.

Use the namespace prefix `sdh` in newly authored source:

```xml
<html xmlns:sdh="https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd">
  <head><title>Example</title></head>
  <body>
    <h1><sdh:keyword term="Example;R example" />Example</h1>
    <sdh:equation>$x^2$</sdh:equation>
    <sdh:dropDown class="rcode">
      <sdh:dropDownHead><sdh:dropDownHotspot>R code</sdh:dropDownHotspot></sdh:dropDownHead>
      <sdh:dropDownBody><pre>mean(c(1, 2, 3))</pre></sdh:dropDownBody>
    </sdh:dropDown>
  </body>
</html>
```

The namespace URI identifies this vocabulary. Runtime help generation recognises
it locally and does not need to contact GitHub or download an XSD. XML prefixes are arbitrary;
namespace-aware code uses the URI. The editor's legacy import compatibility
accepts the old MadCap namespace and preserves the content while normalising the
namespace. That compatibility does not imply that Flare can read every new
StatsDirect extension or that this XSD is equivalent to the full MadCap schema.

The eight element names supported here are `equation`, `keyword`, `dropDown`,
`dropDownHead`, `dropDownHotspot`, `dropDownBody`, `bodyProxy` and `pageFooter`.
Global authoring attributes are `conditions`, `searchable`, `ignoredWords`,
`lastBlockDepth`, `lastHeight` and `lastWidth`. The imported misspelling
`lastBlocdatackDepth` is retained for lossless compatibility; use the correct
spelling for new metadata. `searchable` accepts XML booleans and imported
`True`/`False` values. Depth must be a nonnegative integer; stored dimensions must
be nonnegative decimal numbers. Condition labels remain plain text so that new
target conditions do not require editing the schema.

Equation, footer and dropdown content may mix text with ordinary unnamespaced
HTML, namespaced XHTML, MathML or SVG. Those languages are not redefined here.
Their content uses lax validation, and the XSD does not establish mathematical
correctness, TeX validity, HTML conformance, CSS appearance, link validity or the
meaning of condition expressions. These require the editor's other checks.

This is an extension schema, not a whole-document HTML schema. Existing topics
use unnamespaced `html` roots. The test script validates each extension element
and namespaced authoring attribute independently, including extensions nested
inside ordinary HTML. It does not pretend to validate the HTML wrapper by
loading this XSD alone.

Files such as `.fltoc`, `.fltar`, `.flcts`, `.flvar`, `.flskn` and `.flprj` have
separate, unnamespaced legacy project roots (`CatapultToc`, `CatapultTarget`,
`CatapultTargetFile`, `CatapultConditionTagSet`, `CatapultVariableSet`,
`CatapultSkin` and `CatapultProject`). They are inventoried but are not placed in
the `sdh` namespace or validated as XHTML extensions by this schema. Retaining
their import format does not create a runtime dependency on Flare.

The independent inventory on 2 November 2026 parsed 497 XML-based files among
835 working-source files, including 404 topics and three master pages. It found
1,073 extension elements and 1,243 extension attributes, all covered by this
schema. Other inventoried roots include legacy alias, glossary, destination,
spelling and personal-state formats; their contents are not published or
reclassified as `sdh` elements by this schema.

Run the independent schema and vocabulary checks without starting the editor:

```powershell
& .\Schema\Test-ExtensionSchema.ps1 -SourceProject .\HelpSource
```

The check loads this local XSD with external XML resolution disabled, inventories
the current source, tests representative HTML/MathML/SVG and TeX constructs,
rejects invalid boolean/numeric values and dropdown structure, then validates
all observed extensions. Legacy namespace names are mapped in memory for the
check; it never rewrites the source. Pass `-OutputPath` to retain a JSON report.
