using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_quan_ol_magic_MoSunQuanQuanyu
{
    internal static int MoQuanyuBit(MoQuanyuEffect effect) => effect == MoQuanyuEffect.None ? 0 : 1 << ((int)effect - 1);

    internal static CardConversionSource MoQuanyuSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static string MoQuanyuEffectName(MoQuanyuEffect e) => e switch
    { MoQuanyuEffect.Baihong => "白虹", MoQuanyuEffect.Qingming => "青冥", MoQuanyuEffect.Bixie => "辟邪",
      MoQuanyuEffect.Zidian => "紫电", MoQuanyuEffect.Baili => "百里", MoQuanyuEffect.Liuxing => "流星", _ => "本轮无可选项" };
}
