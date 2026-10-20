using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_duan_jiong_ol_DuanJiong
{
    internal static CardConversionSource DuanJiongSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static int SaoguSuitBit(Suit suit) => suit switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 };
}
