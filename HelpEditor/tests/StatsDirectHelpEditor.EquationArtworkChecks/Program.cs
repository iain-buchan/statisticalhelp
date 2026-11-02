using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : "../../../../../");
string assets = Path.Combine(root, "src/StatsDirectHelpEditor.App/Web");
var catalog = EquationArtworkCatalog.Load(assets);
var failures = new List<string>(); int checks = 0, equations = 0;
void Check(bool passed, string name) { checks++; if (!passed) failures.Add(name); }
var copied = new List<(string source, string destination)>();
catalog.CopyArtwork((source, destination) => copied.Add((source, destination)));
Check(copied.Count == 250 && copied.All(x => x.destination.StartsWith(EquationArtworkCatalog.OutputFolder, StringComparison.Ordinal)), "Copies all 250 unique verified SVGs through the owned output writer");
XNamespace mc = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "HelpSource/Content"), "*.htm", SearchOption.AllDirectories))
{
    var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
    foreach (var source in document.Descendants().Where(element => element.Name.LocalName == "equation" && HelpSourceNamespaces.IsAuthoringNamespace(element.Name.NamespaceName)))
    {
        equations++;
        var rendered = catalog.TryCreateEquation(source, "nested/topic.htm");
        Check(rendered is not null, "Original source resolves: " + file);
        if (rendered is null) continue;
        Check(rendered.Element("img")?.Attribute("src")?.Value.StartsWith("../" + EquationArtworkCatalog.OutputFolder, StringComparison.Ordinal) == true, "Nested topic resolves its verified artwork");
        Check(rendered.Descendants("span").Any(x => ((string?)x.Attribute("class"))?.Contains("help-equation-live", StringComparison.Ordinal) == true), "Accessible source remains available to the independent MathML renderer");
        var migrated = XElement.Parse(source.ToString(SaveOptions.DisableFormatting).Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri));
        Check(EquationArtworkCatalog.SourceKey(migrated) == EquationArtworkCatalog.SourceKey(source) && catalog.TryCreateEquation(migrated, "nested/topic.htm") is not null,
            "Changing only the authoring namespace preserves verified artwork identity");
        var changed = new XElement(source); changed.Value += "+changed";
        Check(catalog.TryCreateEquation(changed, "nested/topic.htm") is null, "Edited source cannot reuse stale SVG");
        var restyled = new XElement(source); restyled.SetAttributeValue("style", "font-size:24pt");
        Check(catalog.TryCreateEquation(restyled, "nested/topic.htm") is null, "Equation style changes invalidate artwork");
    }
}
Check(equations == 258, "Checks all 258 source equations");
Check(catalog.TryCreateEquation(new XElement(mc + "equation", "$new+equation$"), "new.htm") is null, "New equations use the independent renderer");
var a = XElement.Parse("<a:equation xmlns:a='http://www.madcapsoftware.com/Schemas/MadCap.xsd' xmlns:unused='urn:unused' style='font-size:10pt'>$x+λ😀$</a:equation>");
var b = XElement.Parse("<b:equation style='font-size:10pt' xmlns:b='http://www.madcapsoftware.com/Schemas/MadCap.xsd'>$x+λ😀$</b:equation>");
Check(EquationArtworkCatalog.SourceKey(a) == EquationArtworkCatalog.SourceKey(b), "Namespace prefixes/declarations and attribute order do not change a source key");
using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "equation-artwork/catalog.json"))))
    foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
        Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(entry.GetProperty("canonicalSource").GetString()!))) == entry.GetProperty("sourceKey").GetString(), "Captured JavaScript source keys agree with C# SHA256");
var report = new { passed = failures.Count == 0, checkedAtUtc = DateTimeOffset.UtcNow, equations, checks, failures };
File.WriteAllText(Path.Combine(root, "evidence/equation-artwork-core-checks.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
return failures.Count == 0 ? 0 : 1;
