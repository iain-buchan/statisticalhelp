using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

internal static class LearningTargetBuiltinChecks
{
    internal static void Run(string root, string fixture, Action<bool, string> check)
    {
        var starter = TutorCourseDocument.Open(Path.Combine(root, "TutorSource", "starter-course.json"));
        var course = TutorCourseDocument.Create(starter.Content); course.Content["authoring"]!["builtinBaseline"] = TutorBuiltinPublisher.CreateBaseline(starter.Content);
        var question = course.GetItems("questions").Single(row => row["id"]!.GetValue<string>() == "LIB-RATES-01");
        question["difficulty"] = "intermediate"; question["learnerGoals"] = new JsonArray("publicHealth", "postgraduateAlliedHealth"); question["learnerGoalNotes"] = "Practice denominators for public-health and allied-health learners.";
        question["educatorMetadata"] = new JsonObject { ["customCohort"] = "Cohort A", ["preserve"] = true };
        course.Update("questions", "LIB-RATES-01", question); bool refused = false;
        string output = Path.Combine(fixture, "ClassifiedOutput");
        try { TutorBuiltinPublisher.Publish(course, output); } catch (InvalidDataException) { refused = true; }
        check(refused && !Directory.Exists(output), "A classification change cannot reuse an earlier builtin question review");
        course.MarkReviewed("questions", "LIB-RATES-01", "Course author", "Reviewed learner level and goals alongside the question wording.");
        var published = TutorBuiltinPublisher.Publish(course, output);
        var questions = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "questions.json")))!.AsArray();
        var flat = questions.Single(row => row!["id"]!.GetValue<string>() == "LIB-RATES-01")!;
        var lessons = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "lessons.json")))!.AsArray();
        var mirror = lessons.Single(row => row!["id"]!.GetValue<string>() == "rates")!["practiceQuestions"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == "LIB-RATES-01")!;
        var expected = course.GetItems("questions").Single(row => row["id"]!.GetValue<string>() == "LIB-RATES-01");
        string[] fields = ["difficulty", "learnerGoals", "learnerGoalNotes", "educatorMetadata"];
        check(fields.All(field => JsonNode.DeepEquals(flat[field], expected[field]) && JsonNode.DeepEquals(mirror[field], expected[field])) && published.QuestionCount == 35,
            "Builtin publishing preserves classification and unknown metadata in both flat and embedded question data without duplication");
        check(questions.Where(row => row!["id"]!.GetValue<string>() != "LIB-RATES-01").All(row => row!["difficulty"] == null && row["learnerGoals"] == null)
            && flat["track"]!.GetValue<string>() == expected["track"]!.GetValue<string>(),
            "Builtin publication preserves legacy unassigned questions and does not translate goal tags into pathway IDs");
        string previousManifest = File.ReadAllText(Path.Combine(output, "manifest.json"));
        expected["difficulty"] = "expert"; course.Update("questions", "LIB-RATES-01", expected); course.MarkReviewed("questions", "LIB-RATES-01", "Course author", "Review cannot approve an invalid classification identifier.");
        refused = false; try { TutorBuiltinPublisher.Publish(course, output); } catch (InvalidDataException) { refused = true; }
        check(refused && File.ReadAllText(Path.Combine(output, "manifest.json")) == previousManifest,
            "An invalid difficulty cannot replace complete builtin output even when an author review is recorded");
    }
}
