namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_hu_jin_ding_ol_HuJinDingLedger
{
    internal static CardConversionSource HuJinDingSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool IsQingyuanGainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain }];
}
