namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_FixedSlashAndDeclaredDeckPrograms
{
    internal static bool MatchesDeclaredDeckCriterion(Card card, string criterion) => criterion switch
    {
        "basic" => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Basic,
        "trick" => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Trick,
        "equipment" => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Equipment,
        "red" => card.Suit is Suit.Heart or Suit.Diamond,
        "black" => card.Suit is Suit.Spade or Suit.Club,
        _ => throw new InvalidOperationException("Unknown declared deck criterion.")
    };
}
