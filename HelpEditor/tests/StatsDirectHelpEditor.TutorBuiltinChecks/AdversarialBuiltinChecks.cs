using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

internal static class AdversarialBuiltinChecks
{
    internal static void Run(string root,string fixture,Action<bool,string> check)
    {
        var original=TutorCourseDocument.Open(Path.Combine(root,"TutorSource","starter-course.json"));
        TutorCourseDocument Course(){var doc=TutorCourseDocument.Create(original.Content);doc.Content["authoring"]!["builtinBaseline"]=TutorBuiltinPublisher.CreateBaseline(original.Content);return doc;}
        bool Refuses(Action action){try{action();return false;}catch(Exception error)when(error is InvalidDataException or IOException or InvalidOperationException or ArgumentException){return true;}}
        string Qid(TutorCourseDocument doc)=>doc.GetItems("questions")[0]["id"]!.GetValue<string>();
        void Case(string name,Action action){try{action();}catch(Exception error){check(false,name+": "+error.GetType().Name+" "+error.Message);}}
        Case("Builtin optional-string compatibility",()=>
        {
            foreach(string field in new[]{"topic","hint","lesson","help"})
            {
                var doc=Course();string id=Qid(doc);var q=doc.GetItems("questions")[0];q.Remove(field);doc.Update("questions",id,q);doc.MarkReviewed("questions",id,"Course author","Reviewed fixture content.");
                check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Missing-"+field))),"Builtin publication rejects absent runtime-required question string "+field);
            }
            var missing=Course();string qid=Qid(missing);var question=missing.GetItems("questions")[0];question["options"]![0]!.AsObject().Remove("feedback");missing.Update("questions",qid,question);missing.MarkReviewed("questions",qid,"Course author","Reviewed fixture content.");
            check(Refuses(()=>TutorBuiltinPublisher.Publish(missing,Path.Combine(fixture,"Missing-feedback"))),"Builtin publication rejects absent option feedback needed by live practice and saved records");
        });
        Case("Builtin version compatibility",()=>
        {
            var doc=Course();string id=Qid(doc);doc.Content["questions"]![0]!["version"]=0;doc.MarkReviewed("questions",id,"Course author","Reviewed fixture content.");check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Invalid-qversion"))),"Builtin question version must be a positive integer");
        });
        Case("Builtin blank course compatibility",()=>
        {
            var doc=Course();doc.Content["lessons"]=new JsonArray();doc.Content["questions"]=new JsonArray();doc.NewDocument();
            check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Blank-course"))),"Builtin publication requires a lesson even when provider documents make an empty teaching course importable");
        });
        Case("Builtin pathway shape compatibility",()=>
        {
            var doc=Course();doc.Content["authoring"]!["sourceTracks"]!["unsafe\n"]=new JsonObject{["title"]="Extra pathway"};
            check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Invalid-track"))),"Additional pathway identifiers obey the consuming build's strict identifier contract");
            var titled=Course();titled.Content["authoring"]!["sourceTracks"]!["foundation"]!["title"]=new string('x',301);
            check(Refuses(()=>TutorBuiltinPublisher.Publish(titled,Path.Combine(fixture,"Long-track-title"))),"Pathway labels respect the consuming build's title limit");
        });
        Case("New question defaults",()=>
        {
            var doc=Course();var question=doc.NewQuestion();doc.MarkReviewed("questions",question["id"]!.GetValue<string>(),"Course author","Reviewed question defaults for this isolated compatibility fixture.");
            var result=TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"New-question-defaults"));
            check(result.QuestionCount==36,"A new reviewed question includes all default strings required by builtin practice and saved records");
        });
        Case("Prerequisite cycle",()=>
        {
            var doc=Course();var lesson=doc.GetItems("lessons").Single(row=>row["id"]!.GetValue<string>()=="precision");lesson["prerequisites"]=new JsonArray("paired");doc.Update("lessons","precision",lesson);doc.MarkReviewed("lessons","precision","Course author","Reviewed fixture content.");
            check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Cyclic-lessons"))),"Cyclic prerequisites are rejected before writing source that the consuming build cannot use");
        });
        Case("Malformed review metadata",()=>
        {
            var doc=Course();string id=Qid(doc);var question=doc.GetItems("questions")[0];question["explanation"]="A changed explanation.";doc.Update("questions",id,question);doc.MarkReviewed("questions",id,"Course author","Reviewed fixture content.");doc.Content["questions"]![0]!["review"]!["date"]="not-a-date";
            check(Refuses(()=>TutorBuiltinPublisher.Publish(doc,Path.Combine(fixture,"Invalid-review-date"))),"Malformed author-review date cannot publish an unusable changed-item manifest");
        });
        Case("Interrupted publication marker",()=>
        {
            string parent=Path.Combine(fixture,"Interrupted");Directory.CreateDirectory(parent);string output=Path.Combine(parent,"AuthoredTutor");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(parent,".AuthoredTutor.publishing"),"interrupted-owner");
            check(Refuses(()=>TutorBuiltinPublisher.Publish(Course(),output))&&File.ReadAllText(Path.Combine(parent,".AuthoredTutor.publishing"))=="interrupted-owner","An interrupted publication marker is retained and blocks another writer rather than being silently discarded");
        });
    }
}
