using System.Text.Json.Nodes;
using StatsDirect.HelpEditor.Core;

namespace StatsDirect.HelpEditor.App;

public sealed partial class TutorEditorForm
{
    private ComboBox? questionDifficulty;
    private CheckedListBox? questionGoals;
    private bool difficultyEdited, learnerGoalsEdited;
    private sealed record LearningChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void BuildQuestionLearningTargets(TableLayoutPanel table)
    {
        void Add(string label, Control control, int? height = null)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 5, 0) }, 0, row);
            row = table.RowCount++;
            table.RowStyles.Add(height is { } value ? new RowStyle(SizeType.Absolute, value) : new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control, 0, row);
        }
        questionDifficulty = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "MCQ difficulty", Margin = new Padding(3, 4, 3, 9) };
        questionDifficulty.Items.Add(new LearningChoice("", "Not yet assigned"));
        foreach (var pair in TutorLearningTargets.Difficulties) questionDifficulty.Items.Add(new LearningChoice(pair.Key, pair.Value));
        string existing = S(current, "difficulty");
        if (existing.Length > 0 && !TutorLearningTargets.Difficulties.ContainsKey(existing)) questionDifficulty.Items.Add(new LearningChoice(existing, existing + " (unrecognised; choose a level)"));
        questionDifficulty.SelectedItem = questionDifficulty.Items.Cast<LearningChoice>().Single(item => item.Id == existing);
        questionDifficulty.SelectedIndexChanged += (_, _) => { if (!loading) { difficultyEdited = true; Changed(); } };
        Add("Difficulty", questionDifficulty);
        questionGoals = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Target learner goals", Margin = new Padding(3, 4, 3, 9) };
        var goals = (current["learnerGoals"] as JsonArray ?? []).OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var id) ? id : "").Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in TutorLearningTargets.Goals) questionGoals.Items.Add(new LearningChoice(pair.Key, pair.Value), goals.Contains(pair.Key));
        foreach (string id in goals.Where(id => !TutorLearningTargets.Goals.ContainsKey(id))) questionGoals.Items.Add(new LearningChoice(id, id + " (unrecognised; remove or edit JSON)"), true);
        questionGoals.ItemCheck += (_, _) => { if (!loading) { learnerGoalsEdited = true; Changed(); } };
        Add("Target learners — choose all that apply", questionGoals, Math.Max(180, Font.Height * 9 + 16));
    }

    private void ReadQuestionLearningTargets(JsonObject result)
    {
        if (questionDifficulty != null && difficultyEdited)
        {
            string id = (questionDifficulty.SelectedItem as LearningChoice)?.Id ?? "";
            if (id != S(current, "difficulty")) { if (id.Length == 0) result.Remove("difficulty"); else result["difficulty"] = id; }
        }
        if (questionGoals != null && learnerGoalsEdited)
        {
            string[] selected = questionGoals.CheckedItems.Cast<LearningChoice>().Select(item => item.Id).ToArray();
            var original = current["learnerGoals"] as JsonArray;
            bool unchanged = original != null && original.Count == selected.Length && original.All(node => node is JsonValue value && value.TryGetValue<string>(out var id) && selected.Contains(id, StringComparer.Ordinal));
            if (!unchanged && !(original == null && selected.Length == 0)) result["learnerGoals"] = new JsonArray(selected.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }
    }

    private bool SetLearningTargetField(string key, string text)
    {
        if (key == "difficulty" && questionDifficulty != null)
        {
            questionDifficulty.SelectedItem = questionDifficulty.Items.Cast<LearningChoice>().SingleOrDefault(item => item.Id == text) ?? throw new ArgumentException("Choose a supported difficulty.");
            return true;
        }
        if (key == "learnerGoals" && questionGoals != null)
        {
            var ids = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            if (ids.Any(id => !TutorLearningTargets.Goals.ContainsKey(id))) throw new ArgumentException("Choose the listed learner goals; use notes for a specific qualification.");
            for (int index = 0; index < questionGoals.Items.Count; index++) questionGoals.SetItemChecked(index, ids.Contains(((LearningChoice)questionGoals.Items[index]).Id));
            return true;
        }
        return false;
    }

    private string LearningTargetField(string key) => key == "difficulty" ? (questionDifficulty?.SelectedItem as LearningChoice)?.Id ?? ""
        : string.Join("\n", questionGoals?.CheckedItems.Cast<LearningChoice>().Select(item => item.Id) ?? []);

    private static string QuestionLearningTargetText(JsonObject item)
    {
        string difficulty = S(item, "difficulty");
        var labels = (item["learnerGoals"] as JsonArray ?? []).OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var id) && TutorLearningTargets.Goals.TryGetValue(id, out var label) ? label : "");
        return string.Join(" · ", new[] { TutorLearningTargets.Difficulties.GetValueOrDefault(difficulty, difficulty) }.Concat(labels).Append(S(item, "learnerGoalNotes")).Where(value => value.Length > 0));
    }
}
