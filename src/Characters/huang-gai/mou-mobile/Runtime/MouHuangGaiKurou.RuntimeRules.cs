using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huang_gai_mou_mobile_MouHuangGaiKurou
{
    internal static CardConversionSource MouKurouSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    internal static int MouKurouGiftLoss(CardKind kind) => kind is CardKind.Peach or CardKind.Alcohol ? 2 : 1;
}
