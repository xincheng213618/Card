namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RandomDamageTargetSkills
{
    internal static string RandomDamageTargetSkillGrantSource(ProgramSkillFrame f) =>
        $"acquired:damage-target-skill:{f.OwnerSeat}:{f.SkillId}:{f.SkillInstanceId}:{f.Id}";

    internal static bool SameRandomDamageTargetSkillReceipt(ProgramRandomDamageTargetSkillReceipt a, ProgramRandomDamageTargetSkillReceipt b) =>
        a.ProgramFrameId == b.ProgramFrameId && a.InstructionIndex == b.InstructionIndex && a.Source == b.Source &&
        a.GameplayHash == b.GameplayHash && a.DamageWindowId == b.DamageWindowId && a.DamageFrameId == b.DamageFrameId &&
        a.CardUseFrameId == b.CardUseFrameId && a.CardActionId == b.CardActionId && a.TargetSeat == b.TargetSeat &&
        a.DamageAmount == b.DamageAmount && a.Nature == b.Nature && a.SelectedSkillId == b.SelectedSkillId &&
        a.RandomBefore == b.RandomBefore && a.RandomAfter == b.RandomAfter && a.GrantedGrant == b.GrantedGrant &&
        a.Candidates.SequenceEqual(b.Candidates);
}
