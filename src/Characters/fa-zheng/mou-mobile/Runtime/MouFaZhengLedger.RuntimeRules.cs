namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_fa_zheng_mou_mobile_MouFaZhengLedger
{
    internal static CardConversionSource MouFaZhengSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool IsMouFaZhengGainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.TakeMouFaZhengXuanhuoGain }];
}
