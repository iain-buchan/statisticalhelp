using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StatsDirect.HelpEditor.Core;

public static partial class SharedContentPublisher
{
    private static void VerifyHelp(string repository, SharedContentArtifact artifact, string target, string helpSha)
    {
        string root = SafePaths.Resolve(repository, artifact.Path);
        Require(artifact.TopicCount > 0 && artifact.SearchDocumentCount >= 0 && artifact.SearchDocumentCount <= artifact.TopicCount &&
            artifact.LessonCount == null && artifact.QuestionCount == null && artifact.TrackCount == null && artifact.BankVersion == null && artifact.ReviewProofBinding == null,
            "Help artifact counts are invalid.");
        using var info = JsonDocument.Parse(File.ReadAllText(SafePaths.Resolve(root,"build-info.json")));
        Require(info.RootElement.GetProperty("Target").GetString() == target && info.RootElement.GetProperty("TopicCount").GetInt32() == artifact.TopicCount && info.RootElement.GetProperty("SearchDocumentCount").GetInt32() == artifact.SearchDocumentCount, "Help build metadata does not match the shared manifest.");
        int topics = artifact.Files.Count(file => !file.Path.StartsWith("resources/",StringComparison.Ordinal) && file.Path != "index.html" && (file.Path.EndsWith(".htm",StringComparison.Ordinal) || file.Path.EndsWith(".html",StringComparison.Ordinal)));
        Require(topics == artifact.TopicCount && File.Exists(SafePaths.Resolve(root,"index.html")), "The shared help topic inventory is incomplete.");
        string search = File.ReadAllText(SafePaths.Resolve(root,"search-index.js")); const string prefix="window.StatsDirectSearch=";
        Require(search.StartsWith(prefix,StringComparison.Ordinal) && search.TrimEnd().EndsWith(';'), "Shared search data is invalid.");
        using var index=JsonDocument.Parse(search[prefix.Length..].Trim().TrimEnd(';')); var docs=index.RootElement.GetProperty("documents");
        Require(docs.ValueKind==JsonValueKind.Array && docs.GetArrayLength()==artifact.SearchDocumentCount,"Shared search count is invalid.");
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach(var doc in docs.EnumerateArray()) { string url=doc.GetProperty("url").GetString()!; Require(ValidPath(url) && urls.Add(url) && File.Exists(SafePaths.Resolve(root,url)),"Search points to a missing or duplicated help topic."); }
        using var publication=JsonDocument.Parse(File.ReadAllText(SafePaths.Resolve(root,"resources/help-editor-runtime/publication.json")));
        Require(publication.RootElement.GetProperty("sourceSha256").GetString()==helpSha && publication.RootElement.GetProperty("target").GetString()==target,"Help payload is from a different publication snapshot.");
        if(target=="Desktop")
        {
            var receipt=DesktopHelpPublisher.VerifyPortableArtifact(root);
            Require(receipt.PublicationSnapshotSha256==helpSha && receipt.TopicCount==artifact.TopicCount && receipt.SearchDocumentCount==artifact.SearchDocumentCount && receipt.ContextCount==artifact.ContextCount,"Portable Desktop receipt does not match the shared manifest.");
            var aliases=ReadXml(SafePaths.Resolve(root,"Data/Alias.xml")).Descendants("Map").ToArray();
            Require(aliases.Length==artifact.ContextCount && aliases.Select(x=>(string?)x.Attribute("Name")).All(x=>!string.IsNullOrWhiteSpace(x)) && aliases.Select(x=>(string?)x.Attribute("Name")).Distinct(StringComparer.Ordinal).Count()==aliases.Length,"Desktop context aliases are missing or duplicated.");
            using var child=JsonDocument.Parse(File.ReadAllText(SafePaths.Resolve(root,DesktopHelpPublisher.ReceiptFileName)));
            var expected=child.RootElement.GetProperty("contexts").EnumerateArray().ToDictionary(x=>x.GetProperty("name").GetString()!,x=>x,StringComparer.Ordinal);
            foreach(var map in aliases)
            {
                string name=(string)map.Attribute("Name")!,link=(string?)map.Attribute("Link")??""; string topic=link.Split('#','?')[0];
                Require(ValidPath(topic) && File.Exists(SafePaths.Resolve(root,topic)) && expected.TryGetValue(name,out var declared) && declared.GetProperty("link").GetString()==link &&
                    (declared.TryGetProperty("resolvedId",out var numeric)?numeric.GetString():null)==(string?)map.Attribute("ResolvedId"),"Desktop numeric context mapping differs from its receipt.");
            }
        }
        else Require(artifact.ContextCount==null,"Only Desktop help declares contextCount.");
    }
    private static XDocument ReadXml(string path) { using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20000000});return XDocument.Load(reader); }

    private static void VerifyTutor(string root, SharedContentArtifact artifact, string tutorSourceSha256,string desktopRoot)
    {
        Require(artifact.ReviewProofBinding==ReviewProofBinding && artifact.TopicCount==null && artifact.SearchDocumentCount==null && artifact.ContextCount==null &&
            TutorNames.Order(StringComparer.Ordinal).SequenceEqual(artifact.Files.Select(x=>x.Path).Order(StringComparer.Ordinal)),"Tutor payload must contain exactly the six defined export files and its source-proof binding.");
        var manifest=JsonNode.Parse(File.ReadAllText(SafePaths.Resolve(root,"manifest.json")))!.AsObject();
        Require(S(manifest,"sourceFileSha256")==tutorSourceSha256,"Tutor payload is from a different saved source snapshot.");
        var lessons=JsonNode.Parse(File.ReadAllText(SafePaths.Resolve(root,"lessons.json")))!.AsArray(); var questions=JsonNode.Parse(File.ReadAllText(SafePaths.Resolve(root,"questions.json")))!.AsArray(); var tracks=JsonNode.Parse(File.ReadAllText(SafePaths.Resolve(root,"tracks.json")))!.AsObject();
        Require(manifest["schemaVersion"]?.GetValue<int>()==1 && S(manifest,"format")=="statsdirect-builtin-tutor" && manifest["reviewGatePassed"]?.GetValue<bool>()==true && A(manifest,"unreviewedChangedItems").Count==0 && manifest["unreviewedChangedItems"] is JsonArray,"Tutor manifest has not passed its source review gate.");
        Require(lessons.Count>0 && lessons.Count==artifact.LessonCount && questions.Count==artifact.QuestionCount && tracks.Count==artifact.TrackCount && S(manifest,"bankVersion")==artifact.BankVersion && !string.IsNullOrWhiteSpace(artifact.BankVersion) &&
            manifest["lessonCount"]?.GetValue<int>()==lessons.Count && manifest["questionCount"]?.GetValue<int>()==questions.Count && manifest["trackCount"]?.GetValue<int>()==tracks.Count,"Tutor counts/version are inconsistent.");
        var declared=A(manifest,"files"); Require(declared.Count==4 && declared.OfType<JsonObject>().Select(x=>S(x,"path")).Order().SequenceEqual(new[]{"bank.mjs","lessons.json","questions.json","tracks.json"}.Order()),"Tutor child inventory is invalid.");
        foreach(var row in declared.OfType<JsonObject>()) { string path=S(row,"path");Require(FileHash(SafePaths.Resolve(root,path))==S(row,"sha256") && new FileInfo(SafePaths.Resolve(root,path)).Length==row["bytes"]?.GetValue<long>(),"Tutor child file hash/size differs."); }
        Require(FileHash(SafePaths.Resolve(root,"README.md"))==S(manifest,"readmeSha256"),"Tutor README differs from its child manifest.");
        var bank=ParseBank(File.ReadAllText(SafePaths.Resolve(root,"bank.mjs")));
        Require(bank.Version==artifact.BankVersion && JsonNode.DeepEquals(bank.Tracks,tracks) && JsonNode.DeepEquals(bank.Questions,questions),"Tutor static bank does not match its JSON inputs.");
        var ls=Unique(lessons,"lesson"); var qs=Unique(questions,"question");
        foreach(var row in ls.Values.Concat(qs.Values))
        {
            string help=S(row,"help"),operation=S(row,"operation");
            Require(help.Length==0 || ValidPath(help) && File.Exists(SafePaths.Resolve(desktopRoot,help)),"Tutor help binding points outside the published Desktop artifact.");
            Require(operation.Length==0 || Id(operation),"Tutor operation binding is invalid.");
        }
        Require(new[]{"foundation","medicine","publicHealth","analyst","researcher"}.All(tracks.ContainsKey),"Original learning pathway IDs must remain available.");
        foreach(var track in tracks) Require(Id(track.Key) && track.Value is JsonObject value && !string.IsNullOrWhiteSpace(S(value,"title")),"A tutor pathway is invalid.");
        foreach(var lesson in ls.Values)
        {
            Require(!string.IsNullOrWhiteSpace(S(lesson,"title")) && !string.IsNullOrWhiteSpace(S(lesson,"objective")) && !string.IsNullOrWhiteSpace(S(lesson,"summary")),"A tutor lesson is incomplete.");
            foreach(string key in new[]{"prerequisites","assessmentIds","practiceQuestions","sources"}) Require(lesson[key]==null || lesson[key] is JsonArray,"A tutor lesson metadata collection is invalid.");
            foreach(var dependency in A(lesson,"prerequisites")) Require(dependency is JsonValue value && value.TryGetValue<string>(out var id) && id!=S(lesson,"id") && ls.ContainsKey(id),"A lesson prerequisite is invalid.");
            foreach(var question in A(lesson,"assessmentIds")) Require(question is JsonValue value && value.TryGetValue<string>(out var id) && qs.ContainsKey(id),"A lesson assessment link is invalid.");
            foreach(var embedded in A(lesson,"practiceQuestions")) Require(embedded is JsonObject row && qs.TryGetValue(S(row,"id"),out var flat) && JsonNode.DeepEquals(row,flat),"An embedded question differs from its authoritative flat record.");
        }
        foreach(var question in qs.Values)
        {
            Require(question["version"] is JsonValue number && number.TryGetValue<int>(out int version) && version>0 && !string.IsNullOrWhiteSpace(S(question,"stem")) && !string.IsNullOrWhiteSpace(S(question,"explanation")),"A tutor question needs its version, stem and explanation.");
            Require(new[]{"topic","hint","lesson","help"}.All(key=>question[key] is JsonValue text && text.TryGetValue<string>(out _)),"A tutor question is incompatible with saved attempts.");
            Require(S(question,"track")=="core" || tracks.ContainsKey(S(question,"track")),"A tutor question pathway is invalid.");
            var options=A(question,"options"); Require(options.Count is >=2 and <=8 && options.OfType<JsonObject>().Count()==options.Count && options.OfType<JsonObject>().All(option=>Regex.IsMatch(S(option,"id"),"\\A[A-H]\\z") && !string.IsNullOrWhiteSpace(S(option,"text")) && option["feedback"] is JsonValue feedback && feedback.TryGetValue<string>(out _)) && options.OfType<JsonObject>().Select(option=>S(option,"id")).Distinct().Count()==options.Count && options.OfType<JsonObject>().Any(option=>S(option,"id")==S(question,"correct")),"Tutor options/answer key are invalid.");
            Require(!TutorLearningTargets.Validate(question,S(question,"id")).Any(x=>x.Severity=="Error"),"Tutor classification metadata is invalid.");
            string lessonId=S(question,"lessonId"); Require(lessonId.Length==0 || ls.ContainsKey(lessonId),"A tutor question links to a missing lesson.");
            Require(question["sourceIds"]==null || question["sourceIds"] is JsonArray,"Question source IDs must be an array.");
            if(A(question,"sourceIds").Count>0) { Require(ls.TryGetValue(lessonId,out var lesson),"Question source IDs need a linked lesson.");var sources=A(lesson,"sources").OfType<JsonObject>().Select(row=>S(row,"id")).ToHashSet();foreach(var source in A(question,"sourceIds")) Require(source is JsonValue value && value.TryGetValue<string>(out string? id) && sources.Contains(id),"A question source ID is invalid."); }
        }
        var seen=new HashSet<string>(); var active=new HashSet<string>();
        void Visit(string id) { Require(active.Add(id),"Tutor prerequisites form a cycle.");if(seen.Add(id)) foreach(var next in A(ls[id],"prerequisites")) Visit(next!.GetValue<string>());active.Remove(id); }
        foreach(string id in ls.Keys) Visit(id);
        foreach(var row in ls.Values.Concat(qs.Values)) Require(S(row["verification"],"status")!="not-rechecked-after-edit","Tutor numerical evidence is stale.");
        Require(manifest["reviewedChangedItems"] is JsonArray,"Tutor changed-item review declarations are missing.");
        Require(A(manifest,"reviewedChangedItems").OfType<JsonObject>().Count()==A(manifest,"reviewedChangedItems").Count,"A tutor changed-item declaration is invalid.");
        foreach(var item in A(manifest,"reviewedChangedItems").OfType<JsonObject>())
        {
            var collection=S(item,"kind")=="lessons"?ls:S(item,"kind")=="questions"?qs:null;
            Require(collection!=null,"A reviewed tutor item kind is invalid.");
            Require(collection.TryGetValue(S(item,"id"),out var row),"A reviewed tutor item is missing.");
            Require(row!["review"] is JsonObject,"A reviewed tutor declaration has no record proof.");var review=row["review"]!;
            Require(S(review,"status").StartsWith("Reviewed",StringComparison.OrdinalIgnoreCase) && !S(review,"status").Contains("draft",StringComparison.OrdinalIgnoreCase) &&
                new[]{"reviewer","notes","date","contentSha256"}.All(key=>!string.IsNullOrWhiteSpace(S(item,key)) && S(item,key)==S(review,key)) && HashValid(S(item,"contentSha256")) &&
                DateOnly.TryParseExact(S(item,"date"),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _),"A tutor review declaration differs from its source-bound proof.");
        }
    }
    private static Dictionary<string,JsonObject> Unique(JsonArray rows,string kind)
    { var result=new Dictionary<string,JsonObject>(StringComparer.Ordinal);foreach(var item in rows) Require(item is JsonObject row && Id(S(row,"id")) && result.TryAdd(S(row,"id"),row),"Tutor "+kind+" IDs must be unique and stable.");return result; }
    private static bool Id(string text)=>Regex.IsMatch(text,"\\A[A-Za-z0-9_-]{1,100}\\z");
    private static string S(JsonNode? node,string key)=>node is JsonObject obj && obj[key] is JsonValue text && text.TryGetValue<string>(out var value)?value:"";
    private static JsonArray A(JsonNode? node,string key)=>node is JsonObject obj && obj[key] is JsonArray array?array:[];
    private static (string Version,JsonObject Tracks,JsonArray Questions) ParseBank(string source)
    {
        JsonNode Read(string name)
        {
            source=Regex.Replace(source,"\\A(?:\\s|//[^\\n]*(?:\\n|$))*","");var prefix=Regex.Match(source,"\\Aexport const "+name+"\\s*=\\s*");Require(prefix.Success,"Tutor bank must contain only its three static JSON exports.");source=source[prefix.Length..];
            var reader=new Utf8JsonReader(Encoding.UTF8.GetBytes(source));using var doc=JsonDocument.ParseValue(ref reader);var value=JsonNode.Parse(doc.RootElement.GetRawText())!;int characters=Encoding.UTF8.GetCharCount(Encoding.UTF8.GetBytes(source).AsSpan(0,(int)reader.BytesConsumed));source=source[characters..].TrimStart();Require(source.StartsWith(';'),"Tutor bank export is incomplete.");source=source[1..];return value;
        }
        string version=Read("bankVersion").GetValue<string>();var tracks=Read("tracks").AsObject();var questions=Read("questions").AsArray();Require(Regex.Replace(source,"(?:\\s|//[^\\n]*(?:\\n|$))*","")=="","Tutor bank contains executable/unexpected content.");return(version,tracks,questions);
    }
}
