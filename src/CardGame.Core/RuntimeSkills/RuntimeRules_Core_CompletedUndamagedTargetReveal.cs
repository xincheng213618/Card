using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CompletedUndamagedTargetReveal
{
    // Native immediate damage tricks. A separate program Damage child is not damage caused by its ancestral Use.
    internal static bool IsCompletedUndamagedDamageUse(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) || kind is
        CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault or CardKind.QizhengXiangsheng;

    internal static string CompletedUndamagedDiscardReason(ProgramSkillFrame f) =>
        $"skill-program.{f.SkillId}.completed-undamaged-target.discard";

    internal static bool CompletedUndamagedChoiceEquals(PromptChoice a, PromptChoice b) => a.Id == b.Id &&
        a.Cards.SequenceEqual(b.Cards) && a.Targets.SequenceEqual(b.Targets) &&
        a.Parameters.OrderBy(p => p.Key).SequenceEqual(b.Parameters.OrderBy(p => p.Key));

    internal static CardColor? CompletedUndamagedColor(Suit suit) => suit switch
    { Suit.Heart or Suit.Diamond => CardColor.Red, Suit.Spade or Suit.Club => CardColor.Black, _ => null };
}
