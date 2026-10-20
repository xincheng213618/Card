namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_ru_ol_SunRuOlChishi
{
    internal static bool IsSunRuOlChishiTrigger(SkillProgramTrigger t) => t.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardCurrentTurnLastZoneLoss);

    internal static bool SunRuOlLostLastZone(CardMovementBatchContext batch, int seat) => batch.SourceCounts.Any(c =>
        c.Location.OwnerSeat == seat && c.Location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment &&
        c.CountBefore > 0 && c.CountAfter == 0 && batch.Movements.Any(m => m.From == c.Location && m.To != m.From));
}
