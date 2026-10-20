namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageTargetSkillRoundDraw
{
    internal static ProgramSkillGrantIdentity RoundSkillIdentity(SkillGrant grant) => new(grant.GrantId, grant.SkillId, grant.SkillInstanceId, grant.SourceId);

    internal static bool SameRoundSkillIdentity(SkillGrant grant, ProgramSkillGrantIdentity identity) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_DamageTargetSkillRoundDraw.RoundSkillIdentity(grant) == identity;
}
