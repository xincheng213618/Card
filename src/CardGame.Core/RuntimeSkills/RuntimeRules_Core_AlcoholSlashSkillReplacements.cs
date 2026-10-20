namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_AlcoholSlashSkillReplacements
{
    internal static bool IsAlcoholSlashSkillReplacementTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage }];

    internal static string AlcoholSlashReplacementGrantSource(int owner, string skill, string instance) =>
        $"acquired:alcohol-slash-replacement:{owner}:{skill}:{instance}";

    internal static bool SamePermanentGrantIdentity(SkillGrant left, SkillGrant right) => left.GrantId == right.GrantId &&
        left.SkillId == right.SkillId && left.SkillInstanceId == right.SkillInstanceId && left.SourceId == right.SourceId;

    internal static bool SameAlcoholSlashReplacementReceipt(AlcoholSlashSkillReplacementReceipt left, AlcoholSlashSkillReplacementReceipt right) =>
        left.ProgramFrameId == right.ProgramFrameId && left.InstructionIndex == right.InstructionIndex && left.Source == right.Source &&
        left.GameplayHash == right.GameplayHash && left.DamageWindowId == right.DamageWindowId && left.DamageFrameId == right.DamageFrameId &&
        left.CardUseFrameId == right.CardUseFrameId && left.CardActionId == right.CardActionId && left.TargetSeat == right.TargetSeat &&
        left.DamageAmount == right.DamageAmount && left.Nature == right.Nature && left.AlcoholBonus == right.AlcoholBonus &&
        left.GrantedGrant == right.GrantedGrant && left.SharedInstanceGrant == right.SharedInstanceGrant && left.GrantAdded == right.GrantAdded &&
        left.LostSkillIds.SequenceEqual(right.LostSkillIds) && left.RemovedGrants.SequenceEqual(right.RemovedGrants) && left.RevokedGrants.SequenceEqual(right.RevokedGrants);
}
