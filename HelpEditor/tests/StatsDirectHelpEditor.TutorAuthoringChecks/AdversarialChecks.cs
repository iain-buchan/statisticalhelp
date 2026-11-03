#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

internal static class AdversarialChecks
{
    internal static void Run(string root, string fixture, Action<bool,string> check)
    {
        var original=TutorCourseDocument.Open(Path.Combine(root,"TutorSource","starter-course.json"));
        JsonObject Copy()=> (JsonObject)original.Content.DeepClone();
        bool Refuses(Action action){try{action();return false;}catch(Exception error)when(error is InvalidDataException or IOException or ArgumentException or JsonException){return true;}}
        void Case(string name,Action action){try{action();}catch(Exception error){check(false,name+": "+error.GetType().Name+" "+error.Message);}}
        Case("UTF8 BOM",()=>
        {
            string file=Path.Combine(fixture,"utf8-bom-course.json");var bytes=Encoding.UTF8.GetBytes(Copy().ToJsonString());File.WriteAllBytes(file,new byte[]{0xef,0xbb,0xbf}.Concat(bytes).ToArray());
            var document=TutorCourseDocument.Open(file);check(document.GetItems("lessons").Count==12,"Windows UTF8-BOM course source opens without changing its content");
        });
        Case("Unknown field round trip",()=>
        {
            var value=Copy();var unknown=JsonNode.Parse("""{"nullValue":null,"flags":[true,false],"nested":{"u":"μ and ≥","long":123456789012345678901234567890,"list":[{},[],null]}}""")!;
            value["foreignMetadata"]=unknown.DeepClone();value["lessons"]![0]!["foreignLesson"]=unknown.DeepClone();var doc=TutorCourseDocument.Create(value);string file=Path.Combine(fixture,"unknown-fields.json");doc.Save(file);var reopened=TutorCourseDocument.Open(file);
            check(JsonNode.DeepEquals(unknown,reopened.Content["foreignMetadata"])&&JsonNode.DeepEquals(unknown,reopened.Content["lessons"]![0]!["foreignLesson"]),"Save/reopen preserves unknown nested JSON, Unicode, large numbers and explicit nulls");
        });
        Case("Malformed authoring",()=>
        {
            var value=Copy();value["authoring"]="Keep these malformed imported notes";
            check(Refuses(()=>TutorCourseDocument.Create(value)),"Root authoring scalar is rejected before an edit could replace and lose its notes");
            var doc=TutorCourseDocument.Create(Copy());var lesson=doc.GetItems("lessons")[0];string id=lesson["id"]!.GetValue<string>();lesson["authoring"]="Keep malformed record notes";doc.Update("lessons",id,lesson);
            var changed=doc.GetItems("lessons")[0];changed["summary"]="Edited prose";bool rejected=Refuses(()=>doc.Update("lessons",id,changed));
            check(rejected&&doc.GetItems("lessons")[0]["authoring"]!.GetValue<string>()=="Keep malformed record notes","Editing a malformed record cannot silently overwrite its nonobject local authoring data");
        });
        Case("Recovery read purity",()=>
        {
            var value=Copy();value.Remove("authoring");var doc=TutorCourseDocument.Create(value);string before=doc.Content.ToJsonString();var deleted=doc.DeletedItems();
            check(deleted.Count==0&&doc.Content.ToJsonString()==before,"Reading an empty deleted-item list does not mutate or dirty an imported course");
        });
        Case("Damaged recovery preservation",()=>
        {
            var doc=TutorCourseDocument.Create(Copy());doc.Content["authoring"]!["deletedItems"]="Retain this damaged recovery record";
            string before=doc.Content.ToJsonString(),id=doc.GetItems("questions")[0]["id"]!.GetValue<string>();
            check(Refuses(()=>doc.Delete("questions",id))&&doc.Content.ToJsonString()==before,"A damaged recovery ledger blocks deletion without replacing its history or removing the live item");
        });
        Case("Question version exhaustion",()=>
        {
            var value=Copy();value["questions"]![0]!["version"]=int.MaxValue;var doc=TutorCourseDocument.Create(value);string before=doc.Content.ToJsonString();var q=doc.GetItems("questions")[0];q["stem"]="An amended question.";
            check(Refuses(()=>doc.Update("questions",q["id"]!.GetValue<string>(),q))&&doc.Content.ToJsonString()==before,"Exhausted question versions fail clearly without a negative version or partial draft mutation");
        });
        Case("Long course revision counter",()=>
        {
            var value=Copy();value["version"]="course-r2147483647";var doc=TutorCourseDocument.Create(value);doc.Content["title"]="Revised course";string file=Path.Combine(fixture,"revision-counter.json");doc.Save(file);
            check(doc.Content["version"]!.GetValue<string>()=="course-r2147483648","Course revision counters advance beyond Int32 without wrapping or reusing an earlier identity");
        });
        Case("Identifiers",()=>
        {
            var doc=TutorCourseDocument.Create(Copy());doc.Content["id"]="course-id\n";check(doc.Validate().Any(issue=>issue.Severity=="Error"&&issue.Path=="id"),"Course identifiers reject a trailing newline");
            var lesson=doc.GetItems("lessons")[0];lesson["help"]="basics/basics.htm\n";doc.Update("lessons",lesson["id"]!.GetValue<string>(),lesson);check(doc.Validate().Any(issue=>issue.Severity=="Error"&&issue.Path.StartsWith("lessons.")),"Help paths reject a trailing newline");
            doc.Content["provider"]!["reviewEmail"]="support@statisticalhelp.org\n";check(doc.Validate().Any(issue=>issue.Severity=="Error"&&issue.Path=="provider"),"Provider review email rejects trailing newline characters");
        });
        Case("Malformed numeric columns",()=>
        {
            var doc=TutorCourseDocument.Create(Copy());doc.Content["lessons"]![0]!["columns"]=new JsonArray(42);
            check(doc.Validate().Any(issue=>issue.Severity=="Error"&&issue.Path.EndsWith(".columns")),"Malformed numeric column rows produce a validation error rather than an unexpected exception");
        });
        Case("Concurrent editor saves",()=>
        {
            bool allProtected=true,allDraftsPreserved=true;
            for(int iteration=0;iteration<3;iteration++)
            {
                var value=Copy();value["authoring"]!["padding"]=new string('x',3_000_000);string file=Path.Combine(fixture,"concurrent-"+iteration+".json");var seed=TutorCourseDocument.Create(value);seed.Save(file);
                var a=TutorCourseDocument.Open(file);var b=TutorCourseDocument.Open(file);a.Content["title"]="Editor A "+iteration;b.Content["title"]="Editor B "+iteration;
                using var barrier=new Barrier(3);
                Task<bool> Save(TutorCourseDocument doc)=>Task.Run(()=>{barrier.SignalAndWait();try{doc.Save();return true;}catch(IOException){return false;}});
                var first=Save(a);var second=Save(b);barrier.SignalAndWait();Task.WaitAll(first,second);
                allProtected&=first.Result!=second.Result;
                allDraftsPreserved&=(a.Content["title"]!.GetValue<string>()=="Editor A "+iteration&&b.Content["title"]!.GetValue<string>()=="Editor B "+iteration);
            }
            check(allProtected,"Concurrent editors cannot both report successful saves over the same source revision");check(allDraftsPreserved,"A concurrent-save conflict retains each editor's exact open draft");
        });
        Case("Concurrent new source saves",()=>
        {
            string file=Path.Combine(fixture,"concurrent-new-source.json");var value=Copy();value["authoring"]!["padding"]=new string('x',3_000_000);var a=TutorCourseDocument.Create(value);var b=TutorCourseDocument.Create(value);a.Content["title"]="New source A";b.Content["title"]="New source B";
            using var barrier=new Barrier(3);Task<bool> Save(TutorCourseDocument doc)=>Task.Run(()=>{barrier.SignalAndWait();try{doc.Save(file);return true;}catch(IOException){return false;}});
            var first=Save(a);var second=Save(b);barrier.SignalAndWait();Task.WaitAll(first,second);check(first.Result!=second.Result,"Concurrent Save As operations cannot overwrite one another's newly created authoring source");
        });
    }
}
