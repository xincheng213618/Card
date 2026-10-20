namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_diao_chan_ol_magic_MoDiaoChanHuanhuo
{
    internal static CardConversionSource MoDcSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
}
