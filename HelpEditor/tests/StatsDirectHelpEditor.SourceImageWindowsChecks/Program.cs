using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using StatsDirect.HelpEditor.App;
using StatsDirect.HelpEditor.Core;

internal static class Program
{
    static MainForm form = null!;
    static string root="", fixture="";
    static readonly List<object> responses=[];
    static readonly List<JsonElement> networkFailures=[];
    static readonly List<object> checks=[];
    static readonly List<CoreWebView2Frame> frames=[];
    static readonly Dictionary<string, JsonElement> observations=[];
    static int passed,failed;
    const string ImageExpression="Array.from(document.images).filter(i=>/normalcurves|normalhistogram/.test(i.getAttribute('src')||'')).map(i=>({src:i.getAttribute('src'),currentSrc:i.currentSrc,complete:i.complete,width:i.naturalWidth,height:i.naturalHeight,display:getComputedStyle(i).display,rect:i.getBoundingClientRect().toJSON()}))";
    [STAThread] static int Main(string[] args)
    {
        root=Path.GetFullPath(args[0]);
        string testRoot=Path.Combine(Path.GetTempPath(),"sdhe-images-"+Guid.NewGuid().ToString("N")[..8]);fixture=Path.Combine(testRoot,"HelpSource");
        Directory.CreateDirectory(Path.Combine(fixture,"Content","distributions"));Directory.CreateDirectory(Path.Combine(fixture,"Content","resources","Images"));Directory.CreateDirectory(Path.Combine(fixture,"Project","TOCs"));
        File.WriteAllText(Path.Combine(fixture,HelpProject.MarkerFile),"{}");
        File.WriteAllText(Path.Combine(fixture,"Content","contents.htm"),"<html><head><title>Contents</title></head><body><h1>Contents</h1></body></html>");
        File.Copy(Path.Combine(root,"HelpSource","Content","distributions","normal.htm"),Path.Combine(fixture,"Content","distributions","normal.htm"),true);
        foreach(string name in new[]{"normalcurves.png","normalhistogram.png","normalhistogram.svg"})File.Copy(Path.Combine(root,"HelpSource","Content","resources","Images",name),Path.Combine(fixture,"Content","resources","Images",name),true);
        File.WriteAllText(Path.Combine(fixture,"Project","TOCs","STATSDIRECT.fltoc"),"<CatapultToc><TocEntry Title='Contents' Link='/Content/contents.htm'/><TocEntry Title='Normal' Link='/Content/distributions/normal.htm'/></CatapultToc>");
        ApplicationConfiguration.Initialize();
        form=new MainForm(fixture,workspaceOverride:new HelpEditorWorkspace(testRoot,fixture,null)){SuppressDialogs=true,WindowState=FormWindowState.Normal,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new(-16000,0),Size=new(1600,1000)};
        Application.ThreadException+=(_,e)=>Check(false,"Unhandled image-check UI exception",e.Exception.ToString());
        form.Shown+=async(_,_)=>
        {
            try{await Run();}catch(Exception ex){Check(false,"Source-image checks completed",ex.ToString());}
            finally{
                Directory.CreateDirectory(Path.Combine(root,"evidence"));File.WriteAllText(Path.Combine(root,"evidence","source-image-windows-checks.json"),JsonSerializer.Serialize(new{checkedAtUtc=DateTimeOffset.UtcNow,fixture,passed,failed,checks,responses,networkFailures,observations},new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine($"{passed} image checks passed; {failed} failed");
                typeof(MainForm).GetField("closingApproved",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(form,true);form.Close();
            }
        };
        Application.Run(form);return failed==0?0:1;
    }
    static async Task Run()
    {
        await form.Initialization.WaitAsync(TimeSpan.FromSeconds(45));
        var originalSource=Directory.GetFiles(fixture,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
        form.EditorWebView.CoreWebView2.WebResourceResponseReceived+=Record;
        form.EditorWebView.CoreWebView2.FrameCreated+=(_,e)=>Track(e.Frame);
        await form.EditorWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable","{}");
        form.EditorWebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived+=(_,e)=>networkFailures.Add(JsonDocument.Parse(e.ParameterObjectAsJson).RootElement.Clone());
        await form.OpenTopicAsync("distributions/normal.htm");
        await Task.Delay(800);
        var formatted=await Read("Array.from(document.querySelectorAll('#visual-editor img')).filter(i=>/normalcurves|normalhistogram/.test(i.getAttribute('src')||'')).map(i=>({src:i.getAttribute('src'),currentSrc:i.currentSrc,complete:i.complete,width:i.naturalWidth,height:i.naturalHeight,display:getComputedStyle(i).display,rect:i.getBoundingClientRect().toJSON()}))");
        observations.Add("formatted",formatted);
        Check(formatted.GetArrayLength()==3,"Formatted view preserves all three authored Normal images",formatted);
        Check(formatted.EnumerateArray().All(i=>i.GetProperty("width").GetInt32()>0),"Formatted view loads all PNG/SVG images through help-project.invalid",formatted);
        foreach(string image in new[]{"Images/normalcurves.png","images/normalcurves.png"}){
            string request=JsonSerializer.Serialize(new{expression="fetch('https://help-project.invalid/resources/"+image+"').then(r=>({status:r.status,type:r.type,url:r.url})).catch(e=>({error:String(e)}))",returnByValue=true,awaitPromise=true});
            observations.Add(image,JsonDocument.Parse(await form.EditorWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",request)).RootElement.Clone());
        }
        var reloaded=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Ready(object? sender,CoreWebView2WebMessageReceivedEventArgs e){using var d=JsonDocument.Parse(e.WebMessageAsJson);if(d.RootElement.TryGetProperty("action",out var action)&&action.GetString()=="ready")reloaded.TrySetResult(true);}
        form.EditorWebView.CoreWebView2.WebMessageReceived+=Ready;
        form.EditorWebView.CoreWebView2.Reload();
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        form.EditorWebView.CoreWebView2.WebMessageReceived-=Ready;
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setUnifiedViews(true)");
        await form.OpenTopicAsync("distributions/normal.htm");await Task.Delay(500);
        var afterReload=await Read("Array.from(document.querySelectorAll('#visual-editor img')).filter(i=>/normalcurves|normalhistogram/.test(i.getAttribute('src')||'')).map(i=>({src:i.src,width:i.naturalWidth,complete:i.complete}))");
        observations.Add("formattedAfterReload",afterReload);
        Check(afterReload.EnumerateArray().All(i=>i.GetProperty("width").GetInt32()>0),"Reloading the editor after source-host mapping makes every Normal image load",afterReload);
        await form.SwitchWorkspaceAsync("public");await Task.Delay(800);
        JsonElement? browser=null;
        foreach(var frame in frames.ToArray().Reverse())try{var result=JsonDocument.Parse(await frame.ExecuteScriptAsync(ImageExpression)).RootElement.Clone();if(result.GetArrayLength()>0){browser=result;break;}}catch(Exception ex)when(ex is System.Runtime.InteropServices.COMException or InvalidOperationException){}
        if(browser is{} b)observations.Add("browser",b);
        Check(browser is{} b2&&b2.GetArrayLength()==2&&b2.EnumerateArray().All(i=>i.GetProperty("width").GetInt32()>0),"Browser output loads Normal PNG and public SVG",browser);
        await form.SwitchWorkspaceAsync("desktop");await Task.Delay(800);
        form.AppPreviewWebView!.CoreWebView2.WebResourceResponseReceived+=Record;
        var app=JsonDocument.Parse(await form.AppPreviewWebView.ExecuteScriptAsync(ImageExpression)).RootElement.Clone();observations.Add("app",app);
        Check(app.GetArrayLength()==2&&app.EnumerateArray().All(i=>i.GetProperty("width").GetInt32()>0),"App output loads both Normal PNG graphics",app);
        await form.SwitchWorkspaceAsync("edit");
        string second=Path.Combine(Path.GetDirectoryName(fixture)!,"SecondSource");
        foreach(string file in Directory.GetFiles(fixture,"*",SearchOption.AllDirectories)){
            string target=Path.Combine(second,Path.GetRelativePath(fixture,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);
        }
        File.Copy(Path.Combine(second,"Content","resources","Images","normalhistogram.png"),Path.Combine(second,"Content","resources","Images","normalcurves.png"),true);
        await form.OpenProjectAsync(second);await form.OpenTopicAsync("distributions/normal.htm");await Task.Delay(500);
        var switched=await Read("Array.from(document.querySelectorAll('#visual-editor img')).filter(i=>/normalcurves|normalhistogram/.test(i.getAttribute('src')||'')).map(i=>({src:i.src,width:i.naturalWidth,height:i.naturalHeight,complete:i.complete}))");observations.Add("formattedSecondProject",switched);
        Check(switched.GetArrayLength()==3&&switched.EnumerateArray().All(i=>i.GetProperty("width").GetInt32()>0),"Switching to another marked source copy loads its PNG/SVG images without a manual reload",switched);
        Check(switched.EnumerateArray().Single(i=>i.GetProperty("src").GetString()!.Contains("normalcurves.png")).GetProperty("width").GetInt32()==1132,"A same-named image in another project uses that project's bytes rather than stale first-project artwork",switched);
        await form.OpenProjectAsync(fixture);await form.OpenTopicAsync("distributions/normal.htm");await Task.Delay(500);
        var returned=await Read("Array.from(document.querySelectorAll('#visual-editor img')).find(i=>i.src.includes('normalcurves.png')).naturalWidth");observations.Add("formattedReturnedProject",returned);
        Check(returned.GetInt32()==446,"Returning to the first project restores its own source artwork");
        string originalPage=form.CurrentProject.ReadTopic("distributions/normal.htm");
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.setTab('visual');const p=document.querySelector('#visual-editor p');p.append(document.createTextNode(' Edited image probe.'));p.dispatchEvent(new Event('input',{bubbles:true}));");
        string edited=JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()"))!;
        static string[] ImageSources(string xml)=>XDocument.Parse(xml).Descendants().Where(element=>element.Name.LocalName=="img").Select(element=>(string?)element.Attribute("src")??"").Where(src=>src.Contains("normalcurves")||src.Contains("normalhistogram")).ToArray();
        Check(edited.Contains("Edited image probe."),"The image-source round-trip check actually serializes a changed visual draft");
        Check(ImageSources(originalPage).SequenceEqual(ImageSources(edited)),"Runtime image cache identities never leak into saved relative XHTML image references",ImageSources(edited));
        await form.EditorWebView.ExecuteScriptAsync("HelpEditor.undo()");
        Check(JsonSerializer.Deserialize<string>(await form.EditorWebView.ExecuteScriptAsync("HelpEditor.getSource()"))==originalPage,"Normal page visual edits retain one-step Undo with its exact original image source");
        Check(originalSource.All(pair=>pair.Value.AsSpan().SequenceEqual(File.ReadAllBytes(pair.Key))),"Source-image inspection and all view/project switches preserve authored source bytes");
    }
    static void Record(object? sender,CoreWebView2WebResourceResponseReceivedEventArgs e){if(e.Request.Uri.Contains("normalcurves")||e.Request.Uri.Contains("normalhistogram"))responses.Add(new{uri=e.Request.Uri,status=e.Response.StatusCode});}
    static void Track(CoreWebView2Frame f){frames.Add(f);f.FrameCreated+=(_,e)=>Track(e.Frame);}
    static async Task<JsonElement> Read(string expression)=>JsonDocument.Parse(await form.EditorWebView.ExecuteScriptAsync(expression)).RootElement.Clone();
    static void Check(bool value,string name,object? detail=null){if(value)passed++;else failed++;checks.Add(new{name,passed=value,detail});Console.WriteLine($"{(value?"PASS":"FAIL")} {name}");}
}
