using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "../../../../../") : args[0]);
if (args.Contains("--recovery-only")) return RecoveryChecks.Run(root);
string evidence = Path.Combine(root, "evidence");
Directory.CreateDirectory(evidence);
var results = new List<object>();
int passed = 0, failed = 0;
void Check(bool value, string name)
{
    if (value) passed++; else failed++;
    results.Add(new { name, passed = value });
    Console.WriteLine($"{(value ? "PASS" : "FAIL")} {name}");
}
void Reject(Action action, string name)
{
    try { action(); Check(false, name); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
    { Check(true, name); }
}
string Digest(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
Dictionary<string, string> SourceDigests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(x => x, Digest);
bool SameFiles(Dictionary<string, string> before) => before.All(x => File.Exists(x.Key) && Digest(x.Key) == x.Value);
string working = Path.Combine(evidence, "CoreFixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(working, "Content", "basics"));
Directory.CreateDirectory(Path.Combine(working, "Project", "TOCs"));
Directory.CreateDirectory(Path.Combine(working, "Project", "Advanced"));
File.WriteAllText(Path.Combine(working, ".statsdirect-help-editor.json"), "{\"prototypeWorkingCopy\":true}");
File.WriteAllText(Path.Combine(working, "StatsDirect.flprj"), "<CatapultProject MasterToc=\"/Project/TOCs/STATSDIRECT.fltoc\" />");
string topic = """
<?xml version="1.0" encoding="utf-8"?>
<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"><head><title>Example topic α</title></head><body><h1><MadCap:keyword term="PEFR;Paired measurements"/>Example topic α</h1><p id="ref-one">Before &amp; after; 12 &lt; 20.</p><p><MadCap:equation>$\displaystyle \frac{x_1+x_2}{2}$</MadCap:equation></p><p MadCap:conditions="Primary.SWHELP">desktoponlyword</p><p MadCap:conditions="Primary.WEBHELP">publiconlyword</p><MadCap:dropDown class="rcode"><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre xml:space="preserve">x &lt;- c(1, 2)
  mean(x)
# λ stays Unicode</pre></MadCap:dropDownBody></MadCap:dropDown></body></html>
""";
File.WriteAllText(Path.Combine(working, "Content", "basics", "example.htm"), topic, new UTF8Encoding(false));
File.WriteAllText(Path.Combine(working, "Project", "TOCs", "STATSDIRECT.fltoc"), "<CatapultToc Version=\"1\"><TocEntry Title=\"Basics\" Link=\"/Content/basics/example.htm\" BreakType=\"chapter\"><TocEntry Title=\"Example\" Link=\"/Content/basics/example.htm#ref-one\" /></TocEntry></CatapultToc>");
File.WriteAllText(Path.Combine(working, "Project", "Advanced", "STATSDIRECT.flali"), "<CatapultAliasFile><Map Name=\"EXAMPLE\" Link=\"/Content/basics/example.htm\"/></CatapultAliasFile>");
File.WriteAllText(Path.Combine(working, "Project", "Advanced", "SD3ContextID.h"), "#define EXAMPLE 1000\n");

try
{
    Reject(() => HelpProject.Open(Path.Combine(root, "src")), "Refuses a folder without working-copy marker");
    var project = HelpProject.Open(working);
    string designFile = Path.Combine(working, "Project", "HelpEditorDesign.json");
    var authoredBeforeDesign = SourceDigests(working);
    Check(project.GetAppearance(HelpBuildTarget.Desktop) == "flare" && project.GetAppearance(HelpBuildTarget.Public) == "flare" && !File.Exists(designFile), "Both target designs default to Flare without creating a settings file");
    project.SetAppearance(HelpBuildTarget.Desktop, "flare");
    Check(!File.Exists(designFile), "Selecting the existing default does not create design settings");
    project.SetAppearance(HelpBuildTarget.Desktop, "readable");
    Check(HelpProject.Open(working).GetAppearance(HelpBuildTarget.Desktop) == "readable" && SameFiles(authoredBeforeDesign), "Readable Contents is an optional authoring design that preserves all existing source files");
    project.SetAppearance(HelpBuildTarget.Desktop, "clean-blue");
    Check(HelpProject.Open(working).GetAppearance(HelpBuildTarget.Desktop) == "clean-blue" && project.GetAppearance(HelpBuildTarget.Public) == "flare", "Design choice survives reopening and is specific to its target");
    project.SetAppearance(HelpBuildTarget.Public, "warm-paper");
    Check(project.GetAppearance(HelpBuildTarget.Desktop) == "clean-blue" && project.GetAppearance(HelpBuildTarget.Public) == "warm-paper" && SameFiles(authoredBeforeDesign), "Saving both designs preserves every existing authored source file");
    string validDesign = File.ReadAllText(designFile);
    Reject(() => project.SetAppearance(HelpBuildTarget.Desktop, "reader-choice"), "Rejects unknown design keys");
    Reject(() => project.SetAppearance((HelpBuildTarget)999, "flare"), "Rejects an unknown design target");
    Check(File.ReadAllText(designFile) == validDesign, "Invalid design requests leave the settings file unchanged");
    File.WriteAllText(designFile, validDesign.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1,\n  \"futureSetting\": {\"note\": \"preserve me\"}"));
    project.SetAppearance(HelpBuildTarget.Desktop, "flare");
    Check(project.GetAppearance(HelpBuildTarget.Public) == "warm-paper" && File.ReadAllText(designFile).Contains("preserve me"), "Saving one target preserves other settings and the other target");
    project.SetAppearance(HelpBuildTarget.Desktop, "clean-blue");
    validDesign = File.ReadAllText(designFile);
    File.WriteAllText(designFile, "{broken json");
    Reject(() => project.GetAppearance(HelpBuildTarget.Desktop), "Reports malformed design settings before a build");
    Reject(() => project.SetAppearance(HelpBuildTarget.Desktop, "flare"), "Refuses to overwrite malformed design settings");
    Check(File.ReadAllText(designFile) == "{broken json", "Malformed design settings remain available to repair");
    File.WriteAllText(designFile, validDesign);
    Check(project.ListTopics().Count == 1, "Lists source topics");
    Check(project.ListTopics()[0].Title.Contains('α'), "Reads Unicode topic title");
    Check(project.ReadTopic("basics/example.htm") == topic, "Reads source without changing R whitespace or XML");
    Reject(() => project.ReadTopic("../../README.md"), "Blocks parent-path reads");
    Reject(() => project.SaveTopic("../outside.htm", topic), "Blocks parent-path writes");
    Reject(() => project.SaveTopic("basics/example.htm", "<html><body>broken"), "Rejects malformed source before overwriting");
    Check(project.ReadTopic("basics/example.htm") == topic, "Malformed save leaves valid topic intact");
    string revised = topic.Replace("Before &amp; after", "Ultramarinecontext Before &amp; after");
    revised = revised.Replace("</body>", "<pre id=\"cdata-code\"><![CDATA[if (x < 3 && y > 1) {\n  print(\"a & b\")\n}]]></pre><p id=\"Ref-Case\">Case-sensitive anchor.</p><p><a href=\"#Ref-Case\">Local anchor</a></p></body>");
    revised = revised.Replace("</body>", "<p><a id=\"external-example\" href=\"https://help-editor-external.invalid/article?x=1&amp;y=2\" rel=\"nofollow\">External reference example</a></p></body>");
    project.SaveTopic("basics/example.htm", revised);
    Check(project.ReadTopic("basics/example.htm") == revised.Replace(HelpSourceNamespaces.LegacyUri, HelpSourceNamespaces.CurrentUri), "Saves exact source apart from the authoring namespace declaration, including unmodified special blocks");
    var toc = project.LoadContents();
    Check(toc.Count == 1 && toc[0].Children.Count == 1, "Loads nested contents");
    Check(toc[0].Children[0].Link == "basics/example.htm#ref-one", "Preserves contents anchors");
    toc[0].Title = "Renamed basics";
    var section = new TocNode { Title = "New section" };
    toc.Add(section);
    string created = project.CreateTopic("New page with α", "basics");
    section.Children.Add(new TocNode { Title = "New page", Link = created });
    project.SaveContents(toc);
    var reloaded = project.LoadContents();
    Check(reloaded[0].Title == "Renamed basics" && reloaded[1].Children.Count == 1, "Saves renamed and added contents sections");
    var tocXml = XDocument.Load(Path.Combine(working, "Project", "TOCs", "STATSDIRECT.fltoc"));
    Check((string?)tocXml.Root!.Elements("TocEntry").First().Attribute("BreakType") == "chapter", "Retains existing Flare contents attributes");
    Check(project.ListTopics().Count == 2 && project.ReadTopic(created).Contains("New page"), "Creates a new valid source page");
    reloaded[1].Children.Clear(); reloaded[0].Children.Add(new TocNode { Title = "Moved page", Link = created });
    project.SaveContents(reloaded);
    Check(project.LoadContents()[0].Children.Last().Title == "Moved page", "Persists contents reparenting and order");
    Check(project.FindReferences("basics/example.htm").Any(), "Finds references to an existing context-help page");
    Reject(() => project.DeleteTopic("basics/example.htm"), "Protects a page used by context-help aliases");
    string encodedLink = Uri.EscapeDataString(Path.GetFileName(created));
    project.SaveTopic("basics/example.htm", revised.Replace("</body>", "<p><a href=\"" + encodedLink + "\">Linked page</a></p></body>"));
    Check(project.FindReferences(created).Contains("basics/example.htm"), "Finds percent-encoded Unicode references before page deletion");
    Reject(() => project.DeleteTopic(created), "Refuses deletion when another page still links to it");
    project.SaveTopic("basics/example.htm", revised);
    project.DeleteTopic(created);
    Check(project.ListTopics().Count == 1 && !project.LoadContents().SelectMany(n => n.Children).Any(n => n.Link == created), "Deletes an unused page and its contents entry");
    string guarded = project.CreateTopic("Deletion failure example", "basics");
    var guardedContents = project.LoadContents(); guardedContents.Add(new TocNode { Title = "Deletion failure", Link = guarded }); project.SaveContents(guardedContents);
    string guardedPath = Path.Combine(working, "Content", guarded.Replace('/', Path.DirectorySeparatorChar));
    string tocFile = Path.Combine(working, "Project", "TOCs", "STATSDIRECT.fltoc"), guardedTocHash = Digest(tocFile), guardedPageHash = Digest(guardedPath);
    try
    {
        File.SetAttributes(guardedPath, File.GetAttributes(guardedPath) | FileAttributes.ReadOnly);
        Reject(() => project.DeleteTopic(guarded), "Read-only page deletion is refused before changing Contents");
        Check(Digest(tocFile) == guardedTocHash && Digest(guardedPath) == guardedPageHash, "Read-only deletion failure preserves exact page and Contents");
    }
    finally { File.SetAttributes(guardedPath, File.GetAttributes(guardedPath) & ~FileAttributes.ReadOnly); }
    using (var held = File.Open(guardedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Reject(() => project.DeleteTopic(guarded), "Locked page deletion is refused before changing Contents");
        Check(Digest(tocFile) == guardedTocHash && Digest(guardedPath) == guardedPageHash, "Locked deletion failure preserves exact page and Contents");
    }
    try
    {
        File.SetAttributes(tocFile, File.GetAttributes(tocFile) | FileAttributes.ReadOnly);
        Reject(() => project.DeleteTopic(guarded), "Read-only Contents prevents deletion of its page");
        Check(Digest(tocFile) == guardedTocHash && Digest(guardedPath) == guardedPageHash, "Contents write failure leaves the original page and navigation intact");
    }
    finally { File.SetAttributes(tocFile, File.GetAttributes(tocFile) & ~FileAttributes.ReadOnly); }
    project.DeleteTopic(guarded);
    string auditAsset = Path.Combine(working, "Content", "resources", "images", "AuditChart.svg");
    Directory.CreateDirectory(Path.GetDirectoryName(auditAsset)!);
    File.WriteAllText(auditAsset, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"><rect width=\"20\" height=\"10\" /></svg>");
    var before = SourceDigests(working);
    string runtime = Path.Combine(root, "src", "StatsDirectHelpEditor.App", "Web");
    string buildRoot = Path.Combine(evidence, "CoreBuild-" + Guid.NewGuid().ToString("N"));
    var desktop = HelpBuilder.Build(project, Path.Combine(buildRoot, "Desktop"), runtime, HelpBuildTarget.Desktop);
    var publicBuild = HelpBuilder.Build(project, Path.Combine(buildRoot, "Public"), runtime, HelpBuildTarget.Public);
    var sameDraft = HelpBuilder.Build(project, Path.Combine(buildRoot, "SameDraft"), runtime, HelpBuildTarget.Public,
        new Dictionary<string, string> { ["basics/example.htm"] = project.ReadTopic("basics/example.htm") });
    Check(File.ReadAllText(Path.Combine(sameDraft.OutputDirectory, "basics/example.htm")) == File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "basics/example.htm")),
        "A saved-source preview uses exactly the same topic HTML as the ordinary website build");
    var beforeDraft = SourceDigests(working);
    string draftSource = project.ReadTopic("basics/example.htm").Replace("Ultramarinecontext", "UnsavedPreviewSearchword");
    var draftBuild = HelpBuilder.Build(project, Path.Combine(buildRoot, "UnsavedDraft"), runtime, HelpBuildTarget.Public,
        new Dictionary<string, string> { ["basics/example.htm"] = draftSource });
    string draftHtml = File.ReadAllText(Path.Combine(draftBuild.OutputDirectory, "basics/example.htm"));
    Check(draftHtml.Contains("UnsavedPreviewSearchword") && draftHtml.Contains("publiconlyword") && !draftHtml.Contains("desktoponlyword"), "Preview draft receives the real website transformation and target conditions");
    Check(File.ReadAllText(Path.Combine(draftBuild.OutputDirectory, "search-index.js")).Contains("unsavedpreviewsearchword"), "The complete preview search index includes the unsaved page snapshot");
    Check(SameFiles(beforeDraft), "Previewing an unsaved page does not save or rewrite any source file");
    Reject(() => HelpBuilder.Build(project, Path.Combine(buildRoot, "InvalidDraft"), runtime, HelpBuildTarget.Desktop,
        new Dictionary<string, string> { ["basics/example.htm"] = "<html><body>unfinished" }), "Malformed preview drafts cannot produce a misleading output preview");
    Reject(() => HelpBuilder.Build(project, Path.Combine(buildRoot, "ForeignDraft"), runtime, HelpBuildTarget.Public,
        new Dictionary<string, string> { ["../outside.htm"] = draftSource }), "A preview override cannot introduce a page outside the project topic list");
    Check(desktop.TopicCount == 1 && desktop.SearchDocumentCount == 1, "Build returns correct topic/index counts");
    Check(File.Exists(Path.Combine(desktop.OutputDirectory, "index.html")), "Build creates an offline help entry point");
    string desktopTopic = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "basics", "example.htm"));
    string publicTopic = File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "basics", "example.htm"));
    string desktopViewer = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "index.html"));
    string publicViewer = File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "index.html"));
    Check(desktopViewer.Contains("data-help-skin=\"clean-blue\"") && desktopTopic.Contains("data-help-skin=\"clean-blue\"") && publicViewer.Contains("data-help-skin=\"warm-paper\"") && publicTopic.Contains("data-help-skin=\"warm-paper\""), "Builder fixes each target's chosen design in its viewer and direct-entry topics");
    string readerScript = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "help-viewer.js"));
    string publicReaderScript = File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "help-viewer.js"));
    string desktopViewerCss = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "help-viewer.css"));
    string publicViewerCss = File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "help-viewer.css"));
    const string desktopChromeRule = "html[data-help-skin] > body > header > .help-identity,\nhtml[data-help-skin] #search-form{display:none}";
    Check(desktopViewerCss.Contains(desktopChromeRule) && !publicViewerCss.Contains(desktopChromeRule),
        "Only Desktop hides the website banner and search form");
    Check(readerScript.Contains("makeLink('Overview', node.link)") && publicReaderScript.Contains("s.append(makeLink(node.title,node.link))")
        && !publicReaderScript.Contains("makeLink('Overview', node.link)")
        && !readerScript.Contains("__HELP_TOPIC_BRANCH__") && !publicReaderScript.Contains("__HELP_TOPIC_BRANCH__"),
        "Desktop Contents keeps a separate Overview link while Public retains linked parent titles");
    Check(!desktopViewer.Contains("Appearance") && !desktopViewer.Contains("<select") && !readerScript.Contains("localStorage") && !readerScript.Contains("setSkin") && !readerScript.Contains("postMessage") && !desktopTopic.Contains("postMessage"), "Generated help contains no appearance control, stored preference or theme messaging API");
    Check(desktopViewer.Contains("<strong>StatsDirect Help</strong>") && !desktopViewer.Contains("<img class=\"help-logo\"") && !desktopViewer.Contains("rel=\"icon\"") && !desktopViewer.Contains("__HELP_"), "A project without branding retains a readable title without broken image or icon links");
    Check(desktopTopic.Contains("desktoponlyword") && !desktopTopic.Contains("publiconlyword"), "Desktop conditions include only desktop variants");
    Check(publicTopic.Contains("publiconlyword") && !publicTopic.Contains("desktoponlyword"), "Public conditions include only public variants");
    Check(desktopTopic.Contains("<details") && desktopTopic.Contains("<summary"), "Build converts R dropdowns into accessible HTML details");
    Check(desktopTopic.Replace("\r\n", "\n").Contains("  mean(x)\n# λ stays Unicode"), "Build preserves R indentation and Unicode");
    Check(desktopTopic.Contains("\\frac{x_1+x_2}{2}"), "Build retains LaTeX for offline typesetting");
    Check(desktopTopic.Contains("if (x &lt; 3 &amp;&amp; y &gt; 1)") && desktopTopic.Contains("print(\"a &amp; b\")") && !desktopTopic.Contains("<![CDATA["), "Build converts CDATA to visible escaped HTML text without losing R operators");
    Check(desktopTopic.Contains("id=\"Ref-Case\"") && desktopTopic.Contains("href=\"#Ref-Case\""), "Build preserves case-sensitive fragment identifiers");
    Check(desktopTopic.Contains("href=\"https://help-editor-external.invalid/article?x=1&amp;y=2\"") && desktopTopic.Contains("rel=\"nofollow noopener noreferrer\"") && desktopTopic.Contains("target=\"_blank\""), "Generated external links retain their href and rel tokens while opening outside the help frame");
    string generated = string.Join('\n', Directory.GetFiles(desktop.OutputDirectory, "*.js", SearchOption.AllDirectories).Where(p => !p.Contains("mathjax", StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));
    generated += string.Join('\n', Directory.GetFiles(desktop.OutputDirectory, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText));
    Check(generated.Contains("ultramarinecontext", StringComparison.OrdinalIgnoreCase), "Rebuilt index contains newly edited text");
    Check(generated.Contains("PEFR", StringComparison.OrdinalIgnoreCase), "Search includes explicit topic keywords");
    string alias = Path.Combine(desktop.OutputDirectory, "Data", "Alias.xml");
    Check(File.Exists(alias) && File.ReadAllText(alias).Contains("1000"), "Build preserves numeric context-help aliases");
    Check(SameFiles(before), "Building both targets does not modify source");
    string lifecycle = project.CreateTopic("Search lifecycle example", "basics");
    project.SaveTopic(lifecycle, project.ReadTopic(lifecycle).Replace("Write the new help page here.", "Quartzforecast lifecycle phrase."));
    var lifecycleToc = project.LoadContents();
    lifecycleToc.Add(new TocNode { Title = "Search lifecycle", Link = lifecycle });
    project.SaveContents(lifecycleToc);
    var addedBuild = HelpBuilder.Build(project, desktop.OutputDirectory, runtime);
    string addedPage = Path.Combine(desktop.OutputDirectory, lifecycle.Replace('/', Path.DirectorySeparatorChar));
    Check(addedBuild.TopicCount == 2 && File.Exists(addedPage), "Rebuild includes a newly created page");
    Check(File.ReadAllText(Path.Combine(desktop.OutputDirectory, "search-index.js")).Contains("quartzforecast"), "New page text enters the rebuilt search index");
    Check(File.ReadAllText(Path.Combine(desktop.OutputDirectory, "contents-data.js")).Contains("Search lifecycle"), "New contents entries appear in rebuilt navigation");
    string unrelated = Path.Combine(desktop.OutputDirectory, "unrelated-note.txt");
    File.WriteAllText(unrelated, "This file was not generated by the builder.");
    project.DeleteTopic(lifecycle);
    var removedBuild = HelpBuilder.Build(project, desktop.OutputDirectory, runtime);
    Check(removedBuild.TopicCount == 1 && !File.Exists(addedPage), "Rebuild removes an obsolete generated page after source deletion");
    Check(!File.ReadAllText(Path.Combine(desktop.OutputDirectory, "search-index.js")).Contains("quartzforecast"), "Deleted page text leaves the search index");
    Check(File.Exists(unrelated), "Rebuild preserves unrelated files outside its generated-file receipt");
    project.SaveTopic("basics/example.htm", revised.Replace("Ultramarinecontext", "Editedfreshcontext"));
    HelpBuilder.Build(project, desktop.OutputDirectory, runtime);
    string editedIndex = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "search-index.js"));
    Check(editedIndex.Contains("editedfreshcontext") && !editedIndex.Contains("ultramarinecontext"), "Repeated rebuilding replaces edited search terms without stale entries");
    project.SaveTopic("basics/example.htm", revised);
    Reject(() => HelpBuilder.Build(project, Path.Combine(working, "Content"), runtime), "Refuses to build over source Content");
    string protectedInstaller = Path.Combine(buildRoot, "ProtectedInstaller");
    string protectionFixture = Path.Combine(buildRoot, "protection-fixture.json");
    File.WriteAllText(protectionFixture, JsonSerializer.Serialize(new { schemaVersion = 1, protectedPaths = new[] { protectedInstaller } }));
    string? previousProtection = Environment.GetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable);
    try
    {
        Environment.SetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable, protectionFixture);
        Reject(() => HelpBuilder.Build(project, protectedInstaller, runtime), "Refuses the configured installer destination");
    }
    finally { Environment.SetEnvironmentVariable(WorkspaceProtectionSettings.ConfigurationEnvironmentVariable, previousProtection); }
    string foreignOutput = Path.Combine(buildRoot, "Unowned"); Directory.CreateDirectory(foreignOutput); File.WriteAllText(Path.Combine(foreignOutput, "keep.txt"), "Keep me");
    Reject(() => HelpBuilder.Build(project, foreignOutput, runtime), "Refuses an existing unowned output folder");
    string retryOutput = Path.Combine(buildRoot, "RetryAfterFailure");
    var retrySource = SourceDigests(working);
    using (var heldAsset = File.Open(auditAsset, FileMode.Open, FileAccess.Read, FileShare.None))
        Reject(() => HelpBuilder.Build(project, retryOutput, runtime), "Locked source asset fails a build without touching source");
    Check(File.Exists(Path.Combine(retryOutput, ".statsdirect-help-output.json")), "A partial failed build records its ownership for safe retry");
    var retried = HelpBuilder.Build(project, retryOutput, runtime);
    Check(retried.TopicCount == 1 && File.Exists(Path.Combine(retryOutput, "index.html")), "The same output folder can be rebuilt after the asset lock is released");
    Check(SameFiles(retrySource), "Failed build and successful retry preserve every source file");
    string collisionTopic = project.CreateTopic("Foreign collision", "basics");
    string collisionOutput = Path.Combine(desktop.OutputDirectory, collisionTopic.Replace('/', Path.DirectorySeparatorChar));
    File.WriteAllText(collisionOutput, "An independently created file must survive.");
    Reject(() => HelpBuilder.Build(project, desktop.OutputDirectory, runtime), "Build refuses to overwrite an unowned file at a new generated path");
    Check(File.ReadAllText(collisionOutput) == "An independently created file must survive.", "A generated-path collision preserves the foreign file");
    File.Delete(collisionOutput); project.DeleteTopic(collisionTopic);
    HelpBuilder.Build(project, desktop.OutputDirectory, runtime);
    string reservedTopic = Path.Combine(working, "Content", "index.html");
    File.WriteAllText(reservedTopic, "<html><head><title>Authored index</title></head><body><h1>Authored index</h1></body></html>");
    string reservedHash = Digest(reservedTopic), viewerHash = Digest(Path.Combine(desktop.OutputDirectory, "index.html"));
    Reject(() => HelpBuilder.Build(project, desktop.OutputDirectory, runtime), "Source index.html cannot silently collide with the generated viewer");
    Check(Digest(reservedTopic) == reservedHash && Digest(Path.Combine(desktop.OutputDirectory, "index.html")) == viewerHash, "Reserved-name rejection preserves both the authored page and current viewer");
    File.Delete(reservedTopic);
    string footerFolder = Path.Combine(working, "Content", "resources", "masterpages");
    Directory.CreateDirectory(footerFolder);
    Directory.CreateDirectory(Path.Combine(working, "Project", "Targets"));
    File.WriteAllText(Path.Combine(working, "Project", "Targets", "DesktopHelp.fltar"), "<CatapultTarget MasterPage=\"/Content/resources/masterpages/desktop.flmsp\" />");
    File.WriteAllText(Path.Combine(working, "Project", "Targets", "Website.fltar"), "<CatapultTarget MasterPage=\"/Content/resources/masterpages/public.flmsp\" />");
    File.WriteAllText(Path.Combine(footerFolder, "desktop.flmsp"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body><MadCap:bodyProxy/><MadCap:pageFooter><p>Desktop footer <a href=\"../../basics/example.htm#ref-one\">Example</a></p></MadCap:pageFooter><script>outsideFooter()</script></body></html>");
    File.WriteAllText(Path.Combine(footerFolder, "public.flmsp"), "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body><MadCap:pageFooter><p onclick=\"bad()\">Public footer <a href=\"https://example.test/footer\">Website</a></p><script>insideFooter()</script></MadCap:pageFooter></body></html>");
    var footerBefore = SourceDigests(working);
    Check(project.ReadSharedFooter(HelpBuildTarget.Desktop)?.Html.Contains("Desktop footer") == true && project.ReadSharedFooter(HelpBuildTarget.Public)?.Html.Contains("Public footer") == true, "Target-specific shared footers are read from their master pages");
    HelpBuilder.Build(project, desktop.OutputDirectory, runtime, HelpBuildTarget.Desktop);
    string desktopFooterOutput = File.ReadAllText(Path.Combine(desktop.OutputDirectory, "basics", "example.htm"));
    Check(desktopFooterOutput.Contains("help-shared-footer") && desktopFooterOutput.Contains("Desktop footer") && !desktopFooterOutput.Contains("Public footer"), "Desktop output includes its shared footer without the website variant");
    Check(desktopFooterOutput.Contains("href=\"example.htm#ref-one\"") && !desktopFooterOutput.Contains("outsideFooter()"), "Footer-relative links resolve at the topic and master-page scripts are omitted");
    HelpBuilder.Build(project, publicBuild.OutputDirectory, runtime, HelpBuildTarget.Public);
    string publicFooterOutput = File.ReadAllText(Path.Combine(publicBuild.OutputDirectory, "basics", "example.htm"));
    Check(publicFooterOutput.Contains("Public footer") && !publicFooterOutput.Contains("Desktop footer") && !publicFooterOutput.Contains("onclick=") && !publicFooterOutput.Contains("insideFooter()"), "Website shared footer retains text while removing executable attributes and scripts");
    Check(publicFooterOutput.Contains("target=\"_blank\"") && publicFooterOutput.Contains("noopener"), "Footer external links use the same external-window behavior as topic links");
    Check(SameFiles(footerBefore), "Reading and rendering shared footers leaves all source files unchanged");
    var real = HelpProject.Open(Path.Combine(root, "HelpSource"));
    Check(real.ListTopics().Count == 404, "Full copied project loads all 404 real help pages");
    var realBefore = SourceDigests(real.RootPath);
    var full = HelpBuilder.Build(real, Path.Combine(root, "Build", "Desktop"), runtime, HelpBuildTarget.Desktop);
    Check(full.TopicCount == 404 && full.SearchDocumentCount == 403, "Full desktop build retains 404 topics and indexes 403 searchable pages");
    string fullViewer = File.ReadAllText(Path.Combine(full.OutputDirectory, "index.html"));
    string fullNestedTopic = File.ReadAllText(Path.Combine(full.OutputDirectory, "rates", "incidence_and_prevalence.htm"));
    Check(fullViewer.Contains("class=\"help-logo\"") && fullViewer.Contains("src=\"resources/images/sd3helplogo.png\"") && fullViewer.Contains("alt=\"StatsDirect StatisticalHelp\"") && fullViewer.Contains("href=\"resources/images/sd3.ico\"") && fullNestedTopic.Contains("href=\"../resources/images/sd3.ico\""), "Real help includes the Flare header logo and browser icon with correct nested-topic paths");
    Check(Digest(Path.Combine(full.OutputDirectory, "resources", "images", "sd3helplogo.png")) == Digest(Path.Combine(real.RootPath, "Content", "resources", "Images", "sd3helplogo.png")) && Digest(Path.Combine(full.OutputDirectory, "resources", "images", "sd3.ico")) == Digest(Path.Combine(real.RootPath, "Content", "resources", "Images", "sd3.ico")), "Build preserves the original logo and icon bytes");
    Check(File.Exists(Path.Combine(full.OutputDirectory, "rates", "incidence_and_prevalence.htm")), "Full build retains established topic URLs");
    var map = XDocument.Load(Path.Combine(full.OutputDirectory, "Data", "Alias.xml"));
    Check(map.Descendants("Map").Count() >= 400, "Full build preserves the context-help map");
    Check(SameFiles(realBefore), "Full build leaves the supplied working copy untouched");
    File.WriteAllText(Path.Combine(evidence, "full-build.json"), JsonSerializer.Serialize(full, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(evidence, "test-fixture-locations.json"), JsonSerializer.Serialize(new { working, buildRoot }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception ex)
{
    failed++;
    results.Add(new { name = "Unexpected exception", passed = false, error = ex.ToString() });
    Console.Error.WriteLine(ex);
}
File.WriteAllText(Path.Combine(evidence, "core-checks.json"), JsonSerializer.Serialize(new { passed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
