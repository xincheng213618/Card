namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ActionCategoryGifts
{
    internal static IReadOnlyDictionary<string, string> GiftChoiceParameters(ProgramSkillFrame frame,
        string resultBind, string action) => new Dictionary<string, string>
        { ["program-action"] = action, ["frame-id"] = frame.Id.ToString(), ["result-bind"] = resultBind };
}
