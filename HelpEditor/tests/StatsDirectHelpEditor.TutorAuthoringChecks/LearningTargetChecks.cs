#nullable enable
using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;
using StatsDirect.UI.Learning;

internal static class LearningTargetChecks
{
    internal static void Run(string root, string fixture, Action<bool, string> check)
    {
        var original = TutorCourseDocument.Open(Path.Combine(root, "TutorSource", "starter-course.json"));
        string untouched = original.Content.ToJsonString();
        bool Valid(JsonObject record) => TutorLearningTargets.Validate(record, "question").Count == 0;
        check(TutorLearningTargets.Difficulties.Keys.SequenceEqual(new[] { "beginner", "intermediate", "advanced" }) && TutorLearningTargets.Goals.Count == 8,
            "Shared question classification exposes the three difficulty levels and eight canonical learner goals");
        bool immutable = false;
        try { ((IDictionary<string, string>)TutorLearningTargets.Goals).Add("unapproved", "Unapproved goal"); } catch (NotSupportedException) { immutable = true; }
        check(immutable, "Shared learner-goal choices cannot be changed by a caller");
        var unassigned = new JsonObject();
        check(Valid(unassigned) && unassigned.Count == 0 && Valid(new JsonObject { ["difficulty"] = null, ["learnerGoals"] = null, ["learnerGoalNotes"] = null }),
            "Missing or null optional classifications remain unassigned without mutating legacy records");
        foreach (string difficulty in TutorLearningTargets.Difficulties.Keys)
            check(Valid(new JsonObject { ["difficulty"] = difficulty, ["learnerGoals"] = new JsonArray(TutorLearningTargets.Goals.Keys.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()), ["learnerGoalNotes"] = new string('x', 3000) }),
                "Classification accepts " + difficulty + " with all eight goals and boundary-length notes");

        var invalid = new JsonObject[]
        {
            new() { ["difficulty"] = "Beginner" }, new() { ["difficulty"] = "beginner\n" }, new() { ["difficulty"] = "" }, new() { ["difficulty"] = 1 },
            new() { ["learnerGoals"] = "publicHealth" }, new() { ["learnerGoals"] = new JsonArray("publicHealth", "publicHealth") },
            new() { ["learnerGoals"] = new JsonArray("publichealth") }, new() { ["learnerGoals"] = new JsonArray("publicHealth\n") },
            new() { ["learnerGoals"] = new JsonArray((JsonNode?)null) }, new() { ["learnerGoals"] = new JsonArray(1) },
            new() { ["learnerGoals"] = new JsonArray(Enumerable.Repeat((JsonNode?)null, 9).ToArray()) },
            new() { ["learnerGoalNotes"] = new string('x', 3001) }, new() { ["learnerGoalNotes"] = new JsonArray() }
        };
        check(invalid.All(record => !Valid(record)), "Classification rejects unsupported types, unknown or repeated goals, oversized selections and oversized notes");

        var fresh = TutorCourseDocument.Create(original.Content); var added = fresh.NewQuestion();
        check(added["difficulty"]!.GetValue<string>() == "beginner" && added["learnerGoals"] is JsonArray { Count: 0 } && added["track"]!.GetValue<string>() == "foundation"
            && fresh.GetItems("questions").Take(35).All(row => !row.ContainsKey("difficulty") && !row.ContainsKey("learnerGoals")),
            "New questions default to beginner without assigning goals or changing any legacy question or pathway");

        var course = TutorCourseDocument.Create(original.Content); var question = course.GetItems("questions")[0]; string id = question["id"]!.GetValue<string>();
        question["educatorMetadata"] = new JsonObject { ["customTag"] = "Retain original notes", ["nullable"] = null };
        course.Update("questions", id, question); course.MarkReviewed("questions", id, "Course author", "Reviewed original question before classification.");
        question = course.GetItems("questions")[0]; int beforeVersion = question["version"]!.GetValue<int>();
        string track = question["track"]!.GetValue<string>(); var options = question["options"]!.DeepClone(); string correct = question["correct"]!.GetValue<string>();
        question["difficulty"] = "advanced"; question["learnerGoals"] = new JsonArray("healthcareBusinessAnalyst", "mastersStatistics"); question["learnerGoalNotes"] = "Use for NHS commissioning analysts and master's students.";
        course.Update("questions", id, question); question = course.GetItems("questions")[0];
        check(question["version"]!.GetValue<int>() == beforeVersion + 1 && question["review"]!["status"]!.GetValue<string>().Contains("Draft") && question["review"]!["reviewer"] == null,
            "Changing classification advances the question version and invalidates its earlier author review");
        check(question["track"]!.GetValue<string>() == track && question["correct"]!.GetValue<string>() == correct && JsonNode.DeepEquals(question["options"], options),
            "Learner-goal classification does not remap the existing pathway, answer key or distractors");

        string source = Path.Combine(fixture, "classified-source.json"), exported = Path.Combine(fixture, "classified-pack.json");
        course.Save(source); var reopened = TutorCourseDocument.Open(source); reopened.Export(exported); var runtime = CoursePack.Validate(JsonNode.Parse(File.ReadAllText(exported))!.AsObject());
        var savedQuestion = reopened.GetItems("questions")[0]; var exportedQuestion = runtime["questions"]![0]!;
        string[] fields = ["difficulty", "learnerGoals", "learnerGoalNotes", "educatorMetadata"];
        check(fields.All(field => JsonNode.DeepEquals(question[field], savedQuestion[field]) && JsonNode.DeepEquals(question[field], exportedQuestion[field])),
            "Save, reopen and actual runtime course validation preserve classification and unknown educator metadata exactly");
        check(reopened.GetItems("questions").Skip(1).All(row => !row.ContainsKey("difficulty") && !row.ContainsKey("learnerGoals")),
            "Saving a classified question leaves other legacy questions unassigned");

        savedQuestion.Remove("difficulty"); savedQuestion["learnerGoals"] = new JsonArray(); savedQuestion.Remove("learnerGoalNotes"); reopened.Update("questions", id, savedQuestion);
        check(!reopened.GetItems("questions")[0].ContainsKey("difficulty") && reopened.GetItems("questions")[0]["learnerGoals"] is JsonArray { Count: 0 } && !reopened.Validate().Any(issue => issue.Severity == "Error"),
            "An author can clear optional question classifications and keep a valid unassigned question");
        savedQuestion = reopened.GetItems("questions")[0]; savedQuestion["learnerGoals"] = new JsonArray("unrecognisedGoal"); reopened.Update("questions", id, savedQuestion);
        string beforeOutput = File.ReadAllText(exported); bool rejected = false;
        try { reopened.Export(exported); } catch (InvalidDataException) { rejected = true; }
        check(rejected && File.ReadAllText(exported) == beforeOutput && reopened.Validate().Any(issue => issue.Path.StartsWith("questions." + id + ".learnerGoals")),
            "Invalid classifications give a question-specific error and cannot overwrite a complete exported pack");
        check(original.Content.ToJsonString() == untouched, "Classification tests leave the maintained starter and its legacy assignments unchanged");
    }
}
