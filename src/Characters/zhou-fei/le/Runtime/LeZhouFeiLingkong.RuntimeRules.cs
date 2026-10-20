namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_fei_le_LeZhouFeiLingkong
{
    internal static CardConversionSource LingkongSource(ProgramSkillFrame f) =>
        new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool IsLingkongGainTrigger(SkillProgramTrigger t) =>
        t.Effects is [{ Op: SkillProgramEffectOp.MarkLingkongFirstOutsideDrawGain }];
}
