namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_JudgmentPrograms
{
    internal static PromptChoice ProgramJudgmentChoice(string action, string label) =>
        new(new ChoiceId($"program-judgment-trigger.{action}"), label, [], [],
            new Dictionary<string, string> { ["action"] = "program-judgment-trigger-" + action });
}
