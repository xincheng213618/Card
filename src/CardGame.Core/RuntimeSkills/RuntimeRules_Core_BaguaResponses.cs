namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_BaguaResponses
{
    internal static PromptChoice BaguaSkipChoice(bool factionDefense = false) => new(
        new ChoiceId(factionDefense ? "faction-defense.bagua-skip" : "respond.bagua-skip"),
        "不发动【八卦阵】，继续选择是否打出【闪】。", [], [],
        new Dictionary<string, string> { ["response"] = "bagua-skip" });
}
