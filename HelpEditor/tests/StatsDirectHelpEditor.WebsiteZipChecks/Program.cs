using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]), evidence = Path.Combine(Path.GetFullPath(args[0]), "evidence", "WebsiteZipFeature");
Directory.CreateDirectory(evidence);
string fixture = Path.Combine(evidence, "Core-" + Guid.NewGuid().ToString("N"));
var checks = new List<object>(); int failed = 0;
void Check(bool value, string name) { checks.Add(new { name, passed = value }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failed++; }
void Reject(Action action, string name) { try { action(); Check(false, name); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or System.Xml.XmlException) { Check(true, name); } }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string, string> Digests(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
bool Same(string directory, Dictionary<string,string> before) => Digests(directory).OrderBy(p=>p.Key).SequenceEqual(before.OrderBy(p=>p.Key));
void Write(string directory, string relative, string value) { string path=Path.Combine(directory,relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,value,new UTF8Encoding(false)); }
string runtime = Path.Combine(root,"src","StatsDirectHelpEditor.App","Web"), uri=HelpSourceNamespaces.CurrentUri;
HelpProject Fixture(string name)
{
    string path=Path.Combine(fixture,name);
    Write(path,HelpProject.MarkerFile,"{}");
    Write(path,"Project/TOCs/STATSDIRECT.fltoc","<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Example' Link='/Content/basics/example.htm'/><TocEntry Title='About' Link='/Content/menus/about.htm'/></CatapultToc>");
    Write(path,"Content/contents.htm","<html><head><title>Contents</title></head><body><h1>Contents</h1><a href='basics/example.htm#target'>Example</a></body></html>");
    Write(path,"Content/basics/example.htm","<html xmlns:sdh='"+uri+"'><head><title>Example</title></head><body><h1>Example</h1><p id='target'>Saved clarity token.</p><img src='../resources/pixel.svg'/><p sdh:conditions='Primary.SWHELP'>desktopprivateword</p><p sdh:conditions='Primary.WEBHELP'>websitepublicword</p><sdh:equation>$x^2$</sdh:equation></body></html>");
    Write(path,"Content/menus/about.htm","<html xmlns:sdh='"+uri+"' sdh:searchable='False'><head><title>About</title></head><body><h1>About</h1><p>aboutprivatequery</p></body></html>");
    Write(path,"Content/resources/pixel.svg","<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><path d='M0 0L1 1'/></svg>");
    Write(path,"Content/resources/stylesheets/statsdirect.css","body{font:10pt Arial}");
    Write(path,".git/config","private Git configuration"); Write(path,".help-editor-recovery/record.htm","private archived source"); Write(path,"Project/Users/state.xml","private authoring state");
    return HelpProject.Open(path);
}
string Read(ZipArchive zip,string name) { using var reader=new StreamReader(zip.GetEntry(name)!.Open());return reader.ReadToEnd(); }
try
{
    // Produce the full unsaved-draft archive early for independent reader tests.
    var actual=HelpProject.Open(Path.Combine(root,"HelpSource"));var actualBefore=Digests(actual.RootPath);
    const string topic="basics/basics.htm";
    var draft=XDocument.Parse(actual.ReadTopic(topic),LoadOptions.PreserveWhitespace);
    draft.Descendants().First(e=>e.Name.LocalName=="title").Value="ZIP draft review marker";
    var body=draft.Descendants().First(e=>e.Name.LocalName=="body");
    body.Add(XElement.Parse("<p xmlns:sdh='"+uri+"' id='zip-draft-anchor' sdh:conditions='Primary.WEBHELP'>zipwebonlytoken unsaved ZIP review marker.</p>"));
    body.Add(XElement.Parse("<p xmlns:sdh='"+uri+"' sdh:conditions='Primary.SWHELP'>zipdesktoponlytoken</p>"));
    body.Add(XElement.Parse("<sdh:equation xmlns:sdh='"+uri+"'>$\\frac{zipdraftnumerator}{2}$</sdh:equation>"));
    body.Add(XElement.Parse("<sdh:dropDown xmlns:sdh='"+uri+"' class='rcode'><sdh:dropDownHead><sdh:dropDownHotspot>R code ZIP draft</sdh:dropDownHotspot></sdh:dropDownHead><sdh:dropDownBody><pre xml:space='preserve'>zip_draft_values &lt;- c(2, 4, 6)\nmean(zip_draft_values)</pre></sdh:dropDownBody></sdh:dropDown>"));
    Directory.CreateDirectory(fixture);
    string draftSource=draft.ToString(SaveOptions.DisableFormatting), draftPath=Path.Combine(fixture,"draft-source.htm"), draftZip=Path.Combine(fixture,"StatsDirectHelp-Draft.zip");
    File.WriteAllText(draftPath,draftSource,new UTF8Encoding(false));
    var full=WebsiteZipExporter.Export(actual,draftZip,runtime,new Dictionary<string,string>{{topic,draftSource}});
    Check(full.TopicCount==404 && full.SearchDocumentCount==403 && full.Warnings.Count==0,"Full draft export rebuilds404 topics and403 searchable documents without warnings");
    Check(Same(actual.RootPath,actualBefore),"Full draft export preserves every maintained source byte");
    File.WriteAllText(Path.Combine(evidence,"draft-reader-input.json"),JsonSerializer.Serialize(new{zip=draftZip,sourceProject=actual.RootPath,topic,overrideSource=draftPath,expectedTitle="ZIP draft review marker",websiteMarker="zipwebonlytoken",excludedMarker="zipdesktoponlytoken",rMarker="zip_draft_values",anchor="zip-draft-anchor"},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("DRAFT_READER_READY "+draftZip);

    var project=Fixture("Source");var before=Digests(project.RootPath);string zipPath=Path.Combine(fixture,"Deliverables with spaces","Website.zip");
    var result=WebsiteZipExporter.Export(project,zipPath,runtime);
    Check(result.TopicCount==3 && result.SearchDocumentCount==2 && result.FileCount>10 && result.ZipBytes==new FileInfo(zipPath).Length,"Saved export reports exact topic/search/file/byte counts");
    using(var zip=ZipFile.OpenRead(zipPath))
    {
        string[] names=zip.Entries.Select(e=>e.FullName).ToArray();
        Check(names.Contains("index.html") && names.Contains("contents.htm") && names.Contains("basics/example.htm") && names.All(n=>!n.Contains('\\') && !n.StartsWith('/') && !n.Split('/').Contains("..")),"ZIP has a flat portable reader root and relative forward-slash paths");
        Check(!names.Any(n=>n.Contains(".git")||n.Contains(".help-editor-recovery")||n.Contains("Project/")||n.Contains("profile")||n.EndsWith(".flmsp")||n==".statsdirect-help-output.json"),"ZIP excludes source, project, recovery, profile and builder-ownership files");
        string html=Read(zip,"basics/example.htm"),search=Read(zip,"search-index.js");
        Check(html.Contains("websitepublicword")&&!html.Contains("desktopprivateword")&&html.Contains("help-equation")&&names.Contains("resources/pixel.svg"),"Export uses Website conditions and packages equations and relative artwork");
        Check(names.Contains("menus/about.htm")&&!search.Contains("aboutprivatequery")&&Read(zip,"contents-data.js").Contains("menus/about.htm"),"Search exclusions preserve the page and Contents entry");
    }
    Check(Same(project.RootPath,before),"Saved export never modifies authored source or local state");
    string movedPublication = Path.Combine(fixture, "Renamed publication.zip");
    File.Copy(zipPath, movedPublication);
    using (var archive = ZipFile.Open(movedPublication, ZipArchiveMode.Update))
        archive.CreateEntry("resources/help-editor-runtime/publication.json");
    string publicationHash = Hash(movedPublication);
    Reject(() => WebsiteZipExporter.Export(project, movedPublication, runtime), "A moved publication ZIP is protected by its archive entry");
    Check(Hash(movedPublication) == publicationHash, "Refusing to overwrite a publication preserves its exact bytes");

    string history = Path.Combine(fixture, "Custom history");
    string legacyZip = Path.Combine(fixture, "Previously used for publication.zip");
    File.Copy(zipPath, legacyZip);
    string legacyHash = Hash(legacyZip);
    Write(history, Guid.NewGuid().ToString("N") + ".json", JsonSerializer.Serialize(new
    {
        SchemaVersion = 1, Phase = "Failed", Result = new { WebsiteZip = new { ZipPath = legacyZip } }
    }));
    Reject(() => WebsiteZipExporter.Export(project, legacyZip, runtime, publicationHistoryDirectory: history),
        "A partial legacy publication reserves its delivered ZIP path even if its bytes have since changed");
    Check(Hash(legacyZip) == legacyHash, "The legacy publication path retains the existing archive");
    Write(history, Guid.NewGuid().ToString("N") + ".json", JsonSerializer.Serialize(new
    {
        SchemaVersion = 1, Phase = "Failed", Options = new { WebsiteZipPath = zipPath }, Result = new { WebsiteZip = (object?)null }
    }));
    WebsiteZipExporter.Export(project, zipPath, runtime, publicationHistoryDirectory: history);
    Check(File.Exists(zipPath), "An attempt that never delivered a ZIP does not reserve the filename");

    string lockPath = Path.Combine(Path.GetDirectoryName(zipPath)!, ".statsdirect-help-zip.lock");
    string beforeLock = Hash(zipPath);
    using (var locked = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Reject(() => WebsiteZipExporter.Export(project, zipPath, runtime), "A busy destination lock prevents an overlapping replacement");
    Check(Hash(zipPath) == beforeLock, "Lock contention preserves the earlier archive");
    WebsiteZipExporter.Export(project, zipPath, runtime);
    project.SaveTopic("basics/example.htm",project.ReadTopic("basics/example.htm").Replace("Saved clarity token.","Fresh followon token."));
    project.SetAppearance(HelpBuildTarget.Public,"clean-blue");
    WebsiteZipExporter.Export(project,zipPath,runtime);
    using(var zip=ZipFile.OpenRead(zipPath)) Check(Read(zip,"basics/example.htm").Contains("Fresh followon token.")&&Read(zip,"search-index.js").Contains("followon")&&Read(zip,"build-info.json").Contains("clean-blue"),"Follow-on text/settings edits rebuild fresh page, search and selected appearance");
    string zipHash=Hash(zipPath);before=Digests(project.RootPath);
    using(var locked=new FileStream(zipPath,FileMode.Open,FileAccess.Read,FileShare.None)) Reject(()=>WebsiteZipExporter.Export(project,zipPath,runtime),"Locked ZIP destination refuses replacement");
    Check(Hash(zipPath)==zipHash&&Same(project.RootPath,before),"Failed ZIP replacement preserves prior ZIP and all source bytes");
    File.WriteAllText(Path.Combine(project.RootPath,"Content","basics","example.htm"),"<html><body>Malformed");
    Reject(()=>WebsiteZipExporter.Export(project,zipPath,runtime),"Malformed source aborts export before replacing an existing ZIP");
    Check(Hash(zipPath)==zipHash,"Malformed-source failure retains the prior complete archive");
    var privateProject=Fixture("MisplacedPrivate");Write(privateProject.RootPath,"Content/.git/private.htm","<html><body>PRIVATE</body></html>");
    Reject(()=>WebsiteZipExporter.Export(privateProject,zipPath,runtime),"Misplaced private Content cannot leak into pages or search");
    Check(Hash(zipPath)==zipHash,"Private-data rejection preserves the previous ZIP");
    Reject(()=>WebsiteZipExporter.Export(privateProject,Path.Combine(privateProject.RootPath,"Content","site.zip"),runtime),"Source-recursive destination is refused");
    var valid=Fixture("Validation");
    Reject(()=>WebsiteZipExporter.Export(valid,Path.Combine(runtime,"bad-export.zip"),runtime),"Application-assets destination is refused");
    Reject(()=>WebsiteZipExporter.Export(valid,Path.Combine(fixture,"wrong.html"),runtime),"Non-ZIP destination is refused");
    using(var canceled=new CancellationTokenSource()){canceled.Cancel();Reject(()=>WebsiteZipExporter.Export(valid,zipPath,runtime,cancellationToken:canceled.Token),"Pre-cancelled export does not replace ZIP");}
    Check(Hash(zipPath)==zipHash,"Cancelled export preserves the existing complete ZIP");
    using(var cancellation=new CancellationTokenSource())
    {
        cancellation.CancelAfter(1);
        Reject(()=>WebsiteZipExporter.Export(valid,zipPath,runtime,cancellationToken:cancellation.Token),"Cancellation during export generation aborts before promotion");
    }
    Check(Hash(zipPath)==zipHash,"In-flight cancellation preserves ZIP bytes");

    var concurrent=Fixture("ConcurrentSave");
    string large=Path.Combine(concurrent.RootPath,"Content","resources","large.bin");
    File.WriteAllBytes(large,RandomNumberGenerator.GetBytes(8*1024*1024));
    string changedPage=Path.Combine(concurrent.RootPath,"Content","basics","example.htm");
    using(var changed=new ManualResetEventSlim())
    using(var watcher=new FileSystemWatcher(Path.GetDirectoryName(zipPath)!,".sdhe-website-*.tmp"))
    {
        string? callbackError=null;
        watcher.Created+=(_,_)=>{try{File.AppendAllText(changedPage,"<!-- saved by another editor during export -->");}catch(Exception e){callbackError=e.Message;}finally{changed.Set();}};
        watcher.EnableRaisingEvents=true;
        string? failure=null;try{WebsiteZipExporter.Export(concurrent,zipPath,runtime);}catch(InvalidOperationException e){failure=e.Message;}
        Check(changed.Wait(TimeSpan.FromSeconds(5))&&callbackError==null,"Concurrent-save fixture changes source while ZIP is being written");
        Check(failure?.Contains("changed during export")==true&&Hash(zipPath)==zipHash,"Concurrent saved-input changes reject promotion and preserve the prior ZIP");
        Check(File.ReadAllText(changedPage).Contains("saved by another editor"),"A rejected export preserves the other editor's saved changes");
    }
    if(OperatingSystem.IsWindows())
    {
        string outside=Path.Combine(fixture,"JunctionTarget"),link=Path.Combine(fixture,"DestinationJunction");Directory.CreateDirectory(outside);
        async Task Junction(string junction)
        {
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string argument in new[]{"/d","/c","mklink","/J",junction,outside})start.ArgumentList.Add(argument);
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();if(child.ExitCode!=0)throw new IOException(await stdout+await stderr);
        }
        await Junction(link);
        Reject(()=>WebsiteZipExporter.Export(valid,Path.Combine(link,"site.zip"),runtime),"Destination junctions cannot redirect ZIP replacement");
        string sourceLink=Path.Combine(valid.RootPath,"Content","linked-data");await Junction(sourceLink);
        Reject(()=>WebsiteZipExporter.Export(valid,zipPath,runtime),"Source junctions cannot import files outside Content");
        Check(!Directory.GetFiles(outside).Any()&&Hash(zipPath)==zipHash,"Linked-path refusals preserve outside files and the prior ZIP");
    }

    string builder=Path.Combine(root,"src","StatsDirectHelpEditor.Cli","bin","Release","net10.0","StatsDirectHelpBuilder"+(OperatingSystem.IsWindows()?".exe":""));
    async Task<(int Exit,string Output,string Error)> Cli(params string[] options)
    {
        var start=new ProcessStartInfo(builder){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string option in options)start.ArgumentList.Add(option);
        start.Environment["PATH"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32");
        using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();return(child.ExitCode,await output,await error);
    }
    var cliProject=Fixture("CliSource");string cliZip=Path.Combine(fixture,"CLI output","website.zip");
    var cli=await Cli("export-website","--project",cliProject.RootPath,"--zip",cliZip,"--assets",runtime);
    Check(cli.Exit==0&&JsonDocument.Parse(cli.Output).RootElement.GetProperty("TopicCount").GetInt32()==3&&File.Exists(cliZip)&&cli.Error.Length==0,"Packaged CLI exports saved source to a ZIP without SDK/Git/Flare on PATH");
    var blockedCli = await Cli("export-website", "--project", cliProject.RootPath, "--zip", movedPublication, "--assets", runtime);
    Check(blockedCli.Exit == 1 && blockedCli.Error.Contains("publication ZIP") && Hash(movedPublication) == publicationHash,
        "CLI refuses to overwrite a publication ZIP and explains how to retry");
    var legacyCli = await Cli("export-website", "--project", cliProject.RootPath, "--zip", legacyZip,
        "--assets", runtime, "--journal-directory", history);
    Check(legacyCli.Exit == 1 && legacyCli.Error.Contains("publication ZIP") && Hash(legacyZip) == legacyHash,
        "CLI honours the configured custom legacy history");
    foreach(string[] bad in new[]{new[]{"export-website","--project",valid.RootPath},new[]{"export-website","--project",valid.RootPath,"--zip",cliZip,"--target","DesktopHelp"},new[]{"export-website","--project",valid.RootPath,"--zip",cliZip,"--zip",cliZip}})
    {var invalid=await Cli(bad);Check(invalid.Exit==1&&invalid.Output.Length==0&&invalid.Error.Length>0,"Invalid CLI export arguments fail without success JSON");}
}
catch(Exception error){Check(false,"ZIP checks completed: "+error);}
var report=new{checkedAtUtc=DateTimeOffset.UtcNow,passed=failed==0,assertions=checks.Count,failed,fixture,checks};
File.WriteAllText(Path.Combine(evidence,"core-cli-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.assertions,report.failed,report.fixture}));
return failed==0?0:1;
