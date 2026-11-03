using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

internal static class SearchIndexChecks
{
    internal static IReadOnlyList<(string Name, bool Passed)> Run(string fixture)
    {
        var checks = new List<(string, bool)>();
        void Check(bool passed, string name) => checks.Add((name, passed));
        string runtime = Path.Combine(fixture, "SearchRuntime");
        Write(runtime, "mathjax/tex-mml-svg.js", "// Headless search fixture; rendering is tested separately.");
        foreach (var (vocabulary, uri) in new[] { ("Legacy", HelpSourceNamespaces.LegacyUri), ("Current", HelpSourceNamespaces.CurrentUri) })
        {
            string folder = Path.Combine(fixture, "Search" + vocabulary);
            string[] excluded = ["menus/about.htm", "basics/false.htm", "basics/zero.htm", "basics/spaced.htm"];
            string[] included = ["basics/true.htm", "basics/one.htm", "basics/legacytrue.htm", "basics/default.htm", "basics/unrelated.htm", "basics/unqualified.htm"];
            Write(folder, HelpProject.MarkerFile, "{}");
            Write(folder, "Project/TOCs/STATSDIRECT.fltoc", "<CatapultToc>" + string.Concat(excluded.Concat(included).Select(path => "<TocEntry Title='" + path + "' Link='/Content/" + path + "'/>")) + "</CatapultToc>");
            Write(folder, "Project/Advanced/STATSDIRECT.flali", "<CatapultAliasFile><Map Name='ABOUT' Link='/Content/menus/about.htm'/></CatapultAliasFile>");
            Write(folder, "Project/Advanced/SD3ContextID.h", "#define ABOUT 1001\n");
            Write(folder, "Content/resources/stylesheets/statsdirect.css", "body{font:10pt Arial}");
            var flags = new[] { "ext:searchable='False'", "ext:searchable='false'", "ext:searchable='0'", "ext:searchable=' false '",
                "ext:searchable='true'", "ext:searchable='1'", "ext:searchable='True'", "", "other:searchable='false'", "searchable='false'" };
            string Page(string flag, bool searchable) => "<html xmlns:ext='" + uri + "' xmlns:other='urn:unrelated' " + flag + "><head><title>"
                + (searchable ? "Public summary" : "About excluded guidance") + "</title></head><body><h1>"
                + (searchable ? "Public summary" : "About excluded guidance") + "</h1><p>"
                + (searchable ? "publicword R&#233;sum&#233; &amp; 2 &lt; 3." : "aboutprivateword should remain readable.")
                + "</p><ext:keyword term='" + (searchable ? "publickeyword" : "aboutprivatekeyword") + "'/></body></html>";
            var paths = excluded.Concat(included).ToArray();
            for (int index = 0; index < paths.Length; index++) Write(folder, "Content/" + paths[index], Page(flags[index], index >= excluded.Length));
            var project = HelpProject.Open(folder);
            var before = Digests(folder);
            foreach (var target in new[] { HelpBuildTarget.Desktop, HelpBuildTarget.Public })
            {
                string label = vocabulary + " " + target;
                string output = Path.Combine(fixture, "SearchOutput" + vocabulary + target);
                var built = HelpBuilder.Build(project, output, runtime, target);
                using var data = ReadIndex(output);
                var documents = data.RootElement.GetProperty("documents");
                var terms = data.RootElement.GetProperty("terms");
                string[] urls = documents.EnumerateArray().Select(document => document.GetProperty("url").GetString()!).ToArray();
                Check(built.TopicCount == 10 && built.SearchDocumentCount == 6 && built.Warnings.Count == 0 && documents.GetArrayLength() == 6,
                    label + " retains 10 topics while indexing only 6 searchable pages");
                Check(!excluded.Intersect(urls).Any() && included.All(urls.Contains), label + " honors False/false/0 while retaining true/1/default and unrelated attributes");
                Check(!terms.TryGetProperty("about", out _) && !terms.TryGetProperty("aboutprivateword", out _) && !terms.TryGetProperty("aboutprivatekeyword", out _),
                    label + " excluded title, prose and keywords cannot produce search results");
                Check(terms.GetProperty("publicword").GetArrayLength() == 6 && terms.GetProperty("publickeyword").GetArrayLength() == 6
                    && terms.GetProperty("publicword").EnumerateArray().Select(posting => posting[0].GetInt32()).Distinct().Order().SequenceEqual(Enumerable.Range(0, documents.GetArrayLength()))
                    && terms.EnumerateObject().SelectMany(term => term.Value.EnumerateArray()).All(posting => posting[0].GetInt32() >= 0 && posting[0].GetInt32() < documents.GetArrayLength()),
                    label + " ordinary queries and keyword postings resolve to valid contiguous document IDs");
                Check(documents.EnumerateArray().All(document => document.GetProperty("excerpt").GetString()!.Contains("Résumé & 2 < 3.")), label + " searchable prose keeps decoded entities and Unicode");
                Check(File.Exists(Path.Combine(output, "menus", "about.htm")) && File.ReadAllText(Path.Combine(output, "menus", "about.htm")).Contains("aboutprivateword")
                    && File.ReadAllText(Path.Combine(output, "contents-data.js")).Contains("menus/about.htm")
                    && File.ReadAllText(Path.Combine(output, "Data", "Alias.xml")).Contains("ResolvedId=\"1001\""),
                    label + " search exclusion preserves About output, Contents and context help");
                Check(Same(folder, before), label + " search filtering never rewrites source");
            }
            string followOn = Path.Combine(fixture, "SearchFollowOn" + vocabulary);
            project.SaveTopic("menus/about.htm", Page("ext:searchable='true'", false));
            var enabled = HelpBuilder.Build(project, followOn, runtime);
            using (var data = ReadIndex(followOn)) Check(enabled.SearchDocumentCount == 7 && data.RootElement.GetProperty("terms").TryGetProperty("about", out _),
                vocabulary + " explicit follow-on enablement makes the existing page searchable");
            string enabledHtml = File.ReadAllText(Path.Combine(followOn, "menus", "about.htm"));
            project.SaveTopic("menus/about.htm", Page("ext:searchable='0'", false));
            var disabled = HelpBuilder.Build(project, followOn, runtime);
            using (var data = ReadIndex(followOn)) Check(disabled.SearchDocumentCount == 6 && !data.RootElement.GetProperty("terms").TryGetProperty("about", out _)
                && File.ReadAllText(Path.Combine(followOn, "menus", "about.htm")) == enabledHtml,
                vocabulary + " follow-on exclusion removes stale search entries without changing reader HTML");
            var savedBefore = Digests(folder);
            var preview = HelpBuilder.Build(project, followOn, runtime, topicOverrides: new Dictionary<string, string> { ["menus/about.htm"] = Page("ext:searchable='true'", false) });
            Check(preview.SearchDocumentCount == 7 && Same(folder, savedBefore), vocabulary + " draft-preview search flags apply without saving or changing source");
        }
        return checks;
    }

    private static void Write(string root, string relative, string source)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source, new UTF8Encoding(false));
    }
    private static Dictionary<string, string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    private static bool Same(string folder, Dictionary<string, string> before) => Digests(folder).OrderBy(pair => pair.Key).SequenceEqual(before.OrderBy(pair => pair.Key));
    private static JsonDocument ReadIndex(string output)
    {
        string script = File.ReadAllText(Path.Combine(output, "search-index.js"));
        return JsonDocument.Parse(script[script.IndexOf('=')..].TrimStart('=').TrimEnd(';'));
    }
}
