using System.Text;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.Core;

public static partial class SharedContentPublisher
{
    public static TutorBuiltinInspection InspectTutor(string tutorCoursePath, string statsDirectSource, string runtimeAssetsDirectory)
    {
        try { var (course, _) = LoadTutor(tutorCoursePath,statsDirectSource,runtimeAssetsDirectory); return TutorBuiltinPublisher.Inspect(course); }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        { return new(false,0,0,null,[new("Error","course",error.Message)]); }
    }
    private static (TutorCourseDocument Course,string Seed) LoadTutor(string path,string statsDirectSource,string assets)
    {
        path=Path.GetFullPath(path); SafePaths.CheckNoLinks(path); var course=TutorCourseDocument.Open(path);
        string? seed=new[]{Path.Combine(AppContext.BaseDirectory,"TutorSeed/starter-course.json"),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assets))!,"TutorSeed/starter-course.json")}.FirstOrDefault(File.Exists);
        if(seed==null) throw new FileNotFoundException("The immutable packaged tutor baseline is missing. Use the packaged Help Editor/Builder TutorSeed.");
        SafePaths.CheckNoLinks(seed);var original=TutorCourseDocument.Open(seed);
        string repository=DesktopHelpPublisher.InspectDestination(statsDirectSource).RepositoryDirectory;
        string operations=Path.Combine(repository,"StatsDirectUI/Assets/Operations"),help=Path.Combine(repository,"StatsDirectUI/Assets/Help");
        IEnumerable<string>? operationIds=Directory.Exists(operations)?Directory.EnumerateFiles(operations,"*.xml").Select(path=>Path.GetFileNameWithoutExtension(path)!):null;
        IEnumerable<string>? helpPaths=Directory.Exists(help)?Directory.EnumerateFiles(help,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p) is ".htm" or ".html").Select(p=>Path.GetRelativePath(help,p).Replace('\\','/')):null;
        if(course.Content["authoring"] is not JsonObject authoring) {authoring=new();course.Content["authoring"]=authoring;}
        authoring["builtinBaseline"]=TutorBuiltinPublisher.CreateBaseline(original.Content,operationIds,helpPaths);
        return(course,seed);
    }
    internal sealed record PreparedTutorArtifact(string OriginalPath,string SourceSha256,string SeedPath,string SeedSha256,
        TutorBuiltinPublishResult Result,IReadOnlyDictionary<string,string> Payload)
    {
        internal void Verify()
        {
            Require(FileHash(OriginalPath)==SourceSha256 && FileHash(SeedPath)==SeedSha256,"Saved tutor source or its packaged baseline changed during publication.");
            Require(Same(Payload,Files(Result.OutputDirectory)),"The prepared tutor export changed during publication.");
        }
    }
    internal static PreparedTutorArtifact PrepareTutor(string savedPath,string statsDirectSource,string assets,string work)
    {
        savedPath=Path.GetFullPath(savedPath);string sourceSha=FileHash(savedPath);
        var (_,seed)=LoadTutor(savedPath,statsDirectSource,assets);string seedSha=FileHash(seed);
        string frozen=SafePaths.Resolve(work,"TutorSource/"+Path.GetFileName(savedPath));Directory.CreateDirectory(Path.GetDirectoryName(frozen)!);
        using(var input=new FileStream(savedPath,FileMode.Open,FileAccess.Read,FileShare.Read)) using(var output=new FileStream(frozen,FileMode.CreateNew,FileAccess.Write,FileShare.None)) input.CopyTo(output);
        var (course,_)=LoadTutor(frozen,statsDirectSource,assets);
        var result=TutorBuiltinPublisher.Publish(course,Path.Combine(work,"TutorBuild/AuthoredTutor"));
        var prepared=new PreparedTutorArtifact(savedPath,sourceSha,seed,seedSha,result,Files(result.OutputDirectory));prepared.Verify();return prepared;
    }
    internal static Dictionary<string,string>? InspectTutorDestination(string statsDirectSource)
    {
        string repo=DesktopHelpPublisher.InspectDestination(statsDirectSource).RepositoryDirectory;
        string destination=SafePaths.Resolve(repo,"Learning/AuthoredTutor"),marker=SafePaths.Resolve(repo,"Learning/.AuthoredTutor.publishing");
        Require(!File.Exists(marker)&&!Directory.Exists(marker),"Tutor publication is incomplete; resolve its existing marker before publishing.");
        if(File.Exists(destination)) throw new IOException("The tutor output directory is occupied by a file.");
        if(!Directory.Exists(destination)) return null;
        TutorBuiltinPublisher.RequireOwnedOutput(destination);return Files(destination);
    }
    internal static TutorBuiltinPublishResult DeliverTutor(PreparedTutorArtifact prepared,string statsDirectSource,IReadOnlyDictionary<string,string>? expected,CancellationToken ct)
    {
        prepared.Verify();string repo=DesktopHelpPublisher.InspectDestination(statsDirectSource).RepositoryDirectory;
        string destination=SafePaths.Resolve(repo,"Learning/AuthoredTutor"),parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);SafePaths.CheckNoLinks(parent);
        string token=Guid.NewGuid().ToString("N"),marker=Path.Combine(parent,".AuthoredTutor.publishing"),stage=Path.Combine(parent,".AuthoredTutor-stage-"+token),old=Path.Combine(parent,".AuthoredTutor-old-"+token);
        bool moved=false,activated=false,done=false;
        using(var own=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None)) own.Write(Encoding.UTF8.GetBytes(token));
        try
        {
            RequireTarget();Copy(prepared.Result.OutputDirectory,stage,prepared.Payload,ct);TutorBuiltinPublisher.RequireOwnedOutput(stage);prepared.Verify();RequireTarget();ct.ThrowIfCancellationRequested();
            if(Directory.Exists(destination)){Directory.Move(destination,old);moved=true;}
            Directory.Move(stage,destination);activated=true;
            Require(Same(prepared.Payload,Files(destination)),"Activated tutor output differs from its prepared artifact.");
            if(moved) DeleteOwnedTutor(old,expected!);File.Delete(marker);done=true;
            return prepared.Result with {OutputDirectory=destination};
        }
        finally
        {
            if(!activated && moved && !Directory.Exists(destination)) {Directory.Move(old,destination);moved=false;}
            if(!activated && Directory.Exists(stage)) DeleteOwnedTutor(stage,prepared.Payload);
            if(!activated&&!moved&&File.Exists(marker)&&File.ReadAllText(marker)==token) File.Delete(marker);
            // A failed activated output keeps its marker and any transaction
            // evidence; a consumer must fail closed, never certify partial data.
            _=done;
        }
        void RequireTarget()
        {
            if(expected==null) Require(!Directory.Exists(destination)&&!File.Exists(destination),"Tutor output appeared after inspection; it was preserved.");
            else {Require(Directory.Exists(destination),"Tutor output disappeared after inspection.");TutorBuiltinPublisher.RequireOwnedOutput(destination);Require(Same(expected,Files(destination)),"Tutor output changed after inspection; it was preserved.");}
        }
    }
    internal static void Copy(string source,string target,IReadOnlyDictionary<string,string> files,CancellationToken ct)
    {
        if(Directory.Exists(target)||File.Exists(target)) throw new IOException("Prepared publication staging must be new.");
        Directory.CreateDirectory(target);
        foreach(var file in files){ct.ThrowIfCancellationRequested();string path=SafePaths.Resolve(target,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(SafePaths.Resolve(source,file.Key),path);}
        Require(Same(files,Files(target)),"A prepared artifact copy changed.");
    }
    private static bool Same(IReadOnlyDictionary<string,string> left,IReadOnlyDictionary<string,string> right)=>left.Count==right.Count&&left.All(pair=>right.TryGetValue(pair.Key,out var hash)&&hash==pair.Value);
    private static void DeleteOwnedTutor(string path,IReadOnlyDictionary<string,string> expected)
    {
        Require(Same(expected,Files(path)),"Tutor transaction files changed; retained for recovery.");
        foreach(string file in Directory.EnumerateFiles(path)){SafePaths.CheckNoLinks(file);File.Delete(file);}Directory.Delete(path);
    }
}
