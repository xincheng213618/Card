namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SoleIncomingRangeDraw
{
    internal static CardMoveReason SoleIncomingRangeDrawReason(SoleIncomingEffectGrant grant) => new($"skill-program.{grant.Source.SkillId}.sole-incoming-range.draw");
}
