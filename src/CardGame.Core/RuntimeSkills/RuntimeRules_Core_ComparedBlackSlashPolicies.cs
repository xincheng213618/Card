namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ComparedBlackSlashPolicies
{
    internal static void ValidateComparedBlackSlashPolicy(string path, SkillProgramCardPolicy policy)
    {
        if (policy.Kind is not (SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp or SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand)) return;
        if (!policy.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) || policy.Value != 0 ||
            policy.RequiredCardKinds.Count != 0 || policy.InputSuit is not null || policy.OutputSuit is not null ||
            policy.Condition.Kind != SkillProgramConditionKind.Always || policy.FactionId is not null || policy.OwnerRole is not null)
            throw new InvalidOperationException($"{path}: comparison policy requires exactly the three frozen black Slash kinds and no alternate condition.");
    }
}
