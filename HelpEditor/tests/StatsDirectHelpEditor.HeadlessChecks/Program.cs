using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StatsDirect.HelpEditor.Core;

string root = Path.GetFullPath(args[0]), evidence = Path.Combine(root, "evidence");
string fixture = Path.Combine(evidence, "HeadlessFixture-" + Guid.NewGuid().ToString("N"));
string sourceRoot = Path.Combine(fixture, "Source with spaces");
string package = Path.Combine(fixture, "Packaged", "App");
string binaryDirectory = Path.Combine(root, "src", "StatsDirectHelpEditor.Cli", "bin", "Release", "net10.0");
string builder = Path.Combine(package, "Builder", "StatsDirectHelpBuilder.exe");
var checks = new List<object>(); int passed = 0, failed = 0;
Directory.CreateDirectory(evidence);
File.WriteAllText(Path.Combine(evidence, "headless-checks.json"), "{\"completed\":false,\"passed\":false}");
void Check(bool value, string name)
{ if (value) passed++; else failed++; checks.Add(new { name, passed = value }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); }
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
Dictionary<string,string> Digests(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(folder,p),Hash);
bool Same(string folder, Dictionary<string,string> expected) => Digests(folder).OrderBy(p=>p.Key).SequenceEqual(expected.OrderBy(p=>p.Key));
void Write(string relative, string text) { string path=Path.Combine(sourceRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,text,new UTF8Encoding(false)); }
void CopyTree(string from, string to)
{ Directory.CreateDirectory(to); foreach(string path in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){ string dest=Path.Combine(to,Path.GetRelativePath(from,path)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(path,dest); } }
string Page(string title,string body) => "<html xmlns:MadCap='http://www.madcapsoftware.com/Schemas/MadCap.xsd'><head><title>"+title+"</title></head><body><h1>"+title+"</h1>"+body+"</body></html>";
async Task<(int Exit,string Output,string Error)> Run(params string[] arguments)
{
    var start = new ProcessStartInfo(builder) { WorkingDirectory=Path.Combine(fixture,"Unrelated working directory"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
    foreach(string arg in arguments) start.ArgumentList.Add(arg);
    // The apphost loads the installed runtime directly. The builder cannot
    // accidentally depend on an SDK, Flare, Git or a GUI executable on PATH.
    start.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32");
    using var child=Process.Start(start)!;
    var output=child.StandardOutput.ReadToEndAsync(); var error=child.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try { await child.WaitForExitAsync(timeout.Token); }
    catch { child.Kill(true); throw; }
    return(child.ExitCode,await output,await error);
}
try
{
    Directory.CreateDirectory(Path.GetDirectoryName(builder)!);
    foreach(string path in Directory.GetFiles(binaryDirectory)) File.Copy(path,Path.Combine(Path.GetDirectoryName(builder)!,Path.GetFileName(path)));
    CopyTree(Path.Combine(root,"src","StatsDirectHelpEditor.App","Web"),Path.Combine(package,"Web"));
    Directory.CreateDirectory(Path.Combine(fixture,"Unrelated working directory"));
    Write(HelpProject.MarkerFile,"{}");
    Write("StatsDirect.flprj","<CatapultProject/>");
    Write("Project/TOCs/STATSDIRECT.fltoc","<CatapultToc><TocEntry Title='First page' Link='/Content/basics/first.htm#Target'/></CatapultToc>");
    Write("Project/Advanced/STATSDIRECT.flali","<CatapultAliasFile><Map Name='FIRST' Link='/Content/basics/first.htm'/></CatapultAliasFile>");
    Write("Project/Advanced/SD3ContextID.h","#define FIRST 1000\n");
    Write("Project/Targets/DesktopHelp.fltar","<CatapultTarget MasterPage='Content/resources/masterpages/desktop.flmsp'/>");
    Write("Project/Targets/Website.fltar","<CatapultTarget MasterPage='Content/resources/masterpages/web.flmsp'/>");
    Write("Project/Targets/GitHub.fltar","<CatapultTarget MasterPage='Content/resources/masterpages/web.flmsp'/>");
    Write("Content/resources/masterpages/desktop.flmsp","<html xmlns:MadCap='http://www.madcapsoftware.com/Schemas/MadCap.xsd'><body><MadCap:pageFooter><p>Desktop footer marker</p></MadCap:pageFooter></body></html>");
    Write("Content/resources/masterpages/web.flmsp","<html xmlns:MadCap='http://www.madcapsoftware.com/Schemas/MadCap.xsd'><body><MadCap:pageFooter><p>Website footer marker</p></MadCap:pageFooter></body></html>");
    Write("Content/resources/stylesheets/statsdirect.css","body{font:10pt Arial}");
    Write("Content/resources/images/example.svg","<svg xmlns='http://www.w3.org/2000/svg' width='50' height='20'><path d='M0 0L50 20'/></svg>");
    string original = Page("First page α","<p id='Target'>Original clarity phrase.</p><p MadCap:conditions='Primary.SWHELP'>desktoponlyword</p><p MadCap:conditions='Primary.WEBHELP'>publiconlyword</p><p><img src='../resources/images/example.svg' alt='Original chart'/></p><MadCap:equation>$\\frac{x+1}{2}$</MadCap:equation><MadCap:dropDown class='rcode'><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre xml:space='preserve'>x &lt;- c(1, 2)\n  mean(x)\n# λ</pre></MadCap:dropDownBody></MadCap:dropDown>");
    Write("Content/basics/first.htm",original);
    var maintainedBefore=Digests(Path.Combine(root,"HelpSource"));
    var sourceBefore=Digests(sourceRoot);
    var help=await Run("--help");
    Check(help.Exit==0 && help.Output.Contains("DesktopHelp|Website|GitHub") && help.Output.Contains("does not require the .NET SDK"),"Packaged console help documents all targets and runtime-only compilation");
    Check(!File.ReadAllText(Path.Combine(package,"Builder","StatsDirectHelpBuilder.runtimeconfig.json")).Contains("WindowsDesktop"),"The packaged builder has no WinForms/WebView2 desktop-framework dependency");
    var list=await Run("list","--project",sourceRoot);
    Check(list.Exit==0 && JsonDocument.Parse(list.Output).RootElement.GetArrayLength()==1 && list.Error.Length==0,"List works from an unrelated directory with SDK/Flare/Git absent from PATH");
    var targetOutputs=new Dictionary<string,string>();
    foreach(string target in new[]{"DesktopHelp","Website","GitHub"})
    {
        string output=Path.Combine(fixture,"Output with spaces",target);
        var run=await Run("build","--project",sourceRoot,"--output",output,"--target",target);
        Check(run.Exit==0 && run.Error.Length==0,target+" compiles without launching a GUI or SDK command");
        using var report=JsonDocument.Parse(run.Output); var data=report.RootElement;
        Check(data.GetProperty("Target").GetString()==target && data.GetProperty("SourceProject").GetString()==sourceRoot && data.GetProperty("TopicCount").GetInt32()==1 && data.GetProperty("SearchDocumentCount").GetInt32()==1,target+" reports exact target/source and topic/search counts as JSON");
        string html=File.ReadAllText(Path.Combine(output,"basics","first.htm")); targetOutputs[target]=html;
        bool desktop=target=="DesktopHelp";
        Check(html.Contains(desktop?"desktoponlyword":"publiconlyword") && !html.Contains(desktop?"publiconlyword":"desktoponlyword") && html.Contains(desktop?"Desktop footer marker":"Website footer marker"),target+" respects its source conditions and shared footer");
        Check(html.Contains("<details") && html.Contains("mean(x)") && html.Contains("help-equation") && File.Exists(Path.Combine(output,"resources","help-editor-runtime","mathjax","tex-mml-svg.js")),target+" keeps R dropdowns and offline equation rendering");
        Check(File.ReadAllText(Path.Combine(output,"contents-data.js")).Contains("First page") && File.ReadAllText(Path.Combine(output,"search-index.js")).Contains("Original") && File.Exists(Path.Combine(output,"Data","Alias.xml")) && File.ReadAllText(Path.Combine(output,"Data","Alias.xml")).Contains("1000"),target+" rebuilds contents, search and application context IDs");
        Check(Hash(Path.Combine(sourceRoot,"Content","resources","images","example.svg"))==Hash(Path.Combine(output,"resources","images","example.svg")),target+" preserves original SVG artwork bytes");
    }
    Check(targetOutputs["Website"]==targetOutputs["GitHub"],"Website and GitHub use the same Public page rendering");
    var defaultBuild=await Run("build","--project",sourceRoot,"--output",Path.Combine(fixture,"Default target output"));
    Check(defaultBuild.Exit==0 && JsonDocument.Parse(defaultBuild.Output).RootElement.GetProperty("Target").GetString()=="DesktopHelp","An omitted target selects DesktopHelp");
    foreach(string legacy in new[]{"Desktop","Public"})
    {
        string canonical=legacy=="Desktop"?"DesktopHelp":"Website";
        var alias=await Run("build","--project",sourceRoot,"--output",Path.Combine(fixture,"Output with spaces",canonical),"--target",legacy);
        Check(alias.Exit==0 && JsonDocument.Parse(alias.Output).RootElement.GetProperty("Target").GetString()==canonical,"Legacy target alias remains compatible: "+legacy);
    }
    Check(Same(sourceRoot,sourceBefore),"All headless target builds preserve every source file byte");
    Write("Content/basics/first.htm",original.Replace("Original clarity phrase.","New headless searchable phrase."));
    Write("Content/basics/second.htm",Page("New page","<p>New page added outside the GUI.</p>"));
    Write("Project/TOCs/STATSDIRECT.fltoc","<CatapultToc><TocEntry Title='Updated contents' Link='/Content/basics/first.htm#Target'/><TocEntry Title='New page' Link='/Content/basics/second.htm'/></CatapultToc>");
    var editedBefore=Digests(sourceRoot); string website=Path.Combine(fixture,"Output with spaces","Website");
    var rebuilt=await Run("build","--project",sourceRoot,"--output",website,"--target","Website");
    Check(rebuilt.Exit==0 && JsonDocument.Parse(rebuilt.Output).RootElement.GetProperty("TopicCount").GetInt32()==2,"Raw XHTML and new-page edits rebuild successfully without opening the editor");
    Check(File.ReadAllText(Path.Combine(website,"basics","first.htm")).Contains("New headless searchable phrase.") && File.ReadAllText(Path.Combine(website,"search-index.js")).Contains("headless") && File.ReadAllText(Path.Combine(website,"contents-data.js")).Contains("Updated contents") && File.Exists(Path.Combine(website,"basics","second.htm")),"Rebuild refreshes edited page text, search terms, contents and newly added topics");
    Check(Same(sourceRoot,editedBefore),"Rebuilding raw edits never rewrites the edited source");
    foreach(var bad in new[]{new[]{"build","--project",sourceRoot,"--output",website,"--targte","Website"},new[]{"build","--project",sourceRoot,"--project",sourceRoot,"--output",website},new[]{"build","--project",sourceRoot,"--output"},new[]{"build","--project",sourceRoot,"--output",website,"--target","0"},new[]{"list","--project",sourceRoot,"--output",website},new[]{"list","--project"},new[]{"unexpected","--project",sourceRoot}})
    {
        var rejected=await Run(bad); Check(rejected.Exit==1 && rejected.Output.Length==0 && rejected.Error.Length>0,"Invalid arguments fail clearly without success JSON: "+string.Join(' ',bad.Where(x=>!x.StartsWith(fixture,StringComparison.Ordinal))));
    }
    string unowned=Path.Combine(fixture,"Unowned output");Directory.CreateDirectory(unowned);File.WriteAllText(Path.Combine(unowned,"keep.txt"),"KEEP");
    var refused=await Run("build","--project",sourceRoot,"--output",unowned,"--target","Public");
    Check(refused.Exit==1 && File.ReadAllText(Path.Combine(unowned,"keep.txt"))=="KEEP","Unowned output is refused without overwriting its files");
    var sourceOutput=await Run("build","--project",sourceRoot,"--output",Path.Combine(sourceRoot,"Content"),"--target","Desktop");
    Check(sourceOutput.Exit==1 && Same(sourceRoot,editedBefore),"A build cannot overwrite source by choosing Content as output");
    Write("Content/basics/first.htm","<html><body>Malformed raw XHTML");
    var broken=await Run("build","--project",sourceRoot,"--output",website,"--target","Website");
    Check(broken.Exit==1 && broken.Error.Length>0 && broken.Output.Length==0,"Malformed raw XHTML returns failure rather than certifying a successful build");
    string assets=Path.Combine(package,"Web"), moved=Path.Combine(package,"Web temporarily unavailable");
    if(!Path.GetFullPath(assets).StartsWith(fixture+Path.DirectorySeparatorChar,StringComparison.Ordinal) || !Path.GetFullPath(moved).StartsWith(fixture+Path.DirectorySeparatorChar,StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture asset path");
    Directory.Move(assets,moved);
    try
    {
        var unavailable=await Run("build","--project",sourceRoot,"--output",website,"--target","Website");
        Check(unavailable.Exit==1 && unavailable.Error.Contains("Web assets are missing") && unavailable.Output.Length==0,"Missing packaged runtime assets fail clearly rather than silently using another source tree");
        var listWithoutAssets=await Run("list","--project",sourceRoot);
        Check(listWithoutAssets.Exit==1 && listWithoutAssets.Error.Length>0,"Malformed source is still diagnosed by read-only List without needing runtime assets");
    }
    finally { Directory.Move(moved,assets); }
    Check(Same(Path.Combine(root,"HelpSource"),maintainedBefore),"Headless checks never change the maintained help source");
}
catch(Exception ex){Check(false,"Headless test completion: "+ex);}
finally
{
    File.WriteAllText(Path.Combine(evidence,"headless-checks.json"),JsonSerializer.Serialize(new{checkedAtUtc=DateTimeOffset.UtcNow,completed=true,passed=failed==0,passedCount=passed,failedCount=failed,checks,fixture,packagedExecutable=builder,guiStarted=false,sdkOnChildPath=false},new JsonSerializerOptions{WriteIndented=true}));
}
Console.WriteLine($"{passed} headless checks passed; {failed} failed");
return failed==0?0:1;
