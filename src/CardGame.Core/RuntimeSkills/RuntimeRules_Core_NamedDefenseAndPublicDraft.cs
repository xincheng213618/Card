namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NamedDefenseAndPublicDraft
{
    internal static string DefenseNameUsage(string ledger, CardKind kind) => $"declared-name:{ledger}:{CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(kind)}";

    internal static PromptChoice NamedDraftChoice(ProgramSkillFrame frame, string action, string label, int? card = null, int? slot = null, CardKind? name = null, int? seat = null) =>
        new(new ChoiceId($"named-draft.{frame.Id}.{action}.{card?.ToString() ?? slot?.ToString() ?? name?.ToString() ?? seat?.ToString() ?? "none"}"), label,
            card is { } id ? [id] : [], seat is { } target ? [target] : [], new Dictionary<string, string>
            { ["program-action"] = action, ["frame-id"] = frame.Id.ToString(), ["slot-index"] = slot?.ToString() ?? "", ["card-name"] = name?.ToString() ?? "" });
}
