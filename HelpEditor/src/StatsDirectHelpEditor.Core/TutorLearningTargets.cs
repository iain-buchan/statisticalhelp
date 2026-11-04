using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace StatsDirect.HelpEditor.Core;

/// <summary>Optional editorial classification for tutor questions, independent of their existing learning pathway.</summary>
public static class TutorLearningTargets
{
    public static IReadOnlyDictionary<string, string> Difficulties { get; } = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["beginner"] = "Beginner", ["intermediate"] = "Intermediate", ["advanced"] = "Advanced"
        });

    public static IReadOnlyDictionary<string, string> Goals { get; } = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["undergraduateMedicine"] = "Undergraduate medicine",
            ["undergraduateHealthScience"] = "Undergraduate health sciences",
            ["postgraduateMedicine"] = "Postgraduate medicine",
            ["postgraduateAlliedHealth"] = "Postgraduate allied health",
            ["publicHealth"] = "Public health",
            ["healthcareBusinessAnalyst"] = "Healthcare business analyst",
            ["mastersStatistics"] = "Master's-level statistics",
            ["usMedicalBoards"] = "US medical board examinations"
        });

    /// <summary>Validate without assigning defaults or changing legacy questions. Missing/null values remain unassigned.</summary>
    public static IReadOnlyList<TutorValidationIssue> Validate(JsonObject record, string path)
    {
        ArgumentNullException.ThrowIfNull(record);
        var issues = new List<TutorValidationIssue>();
        if (record["difficulty"] is { } difficulty &&
            !(difficulty is JsonValue value && value.TryGetValue<string>(out var id) && Difficulties.ContainsKey(id)))
            issues.Add(new("Error", path + ".difficulty", "Choose beginner, intermediate or advanced, or leave difficulty unassigned."));

        if (record["learnerGoals"] is { } goals)
        {
            if (goals is not JsonArray selected)
                issues.Add(new("Error", path + ".learnerGoals", "Learner goals must be an array of up to eight distinct supported goal IDs."));
            else
            {
                if (selected.Count > Goals.Count)
                    issues.Add(new("Error", path + ".learnerGoals", "Choose no more than eight learner goals."));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < selected.Count; index++)
                {
                    if (selected[index] is not JsonValue goal || !goal.TryGetValue<string>(out var goalId) || !Goals.ContainsKey(goalId))
                        issues.Add(new("Error", path + ".learnerGoals[" + index + "]", "Choose a supported learner-goal ID from the available goals."));
                    else if (!seen.Add(goalId))
                        issues.Add(new("Error", path + ".learnerGoals[" + index + "]", "Each learner goal can be selected only once."));
                }
            }
        }
        if (record["learnerGoalNotes"] is { } notes &&
            !(notes is JsonValue text && text.TryGetValue<string>(out var content) && content.Length <= 3000))
            issues.Add(new("Error", path + ".learnerGoalNotes", "Learner-goal notes must be text of at most 3,000 characters."));
        return issues.AsReadOnly();
    }
}
