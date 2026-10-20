using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_tao_qian_ol_Zhaohuo
{
    internal static bool IsZhaohuoTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveZhaohuoFirstVictimGift }];

    internal static CardConversionSource ZhaohuoSource(ProgramSkillFrame f) =>
        new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
}
