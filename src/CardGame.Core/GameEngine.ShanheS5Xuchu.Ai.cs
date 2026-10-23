namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TrySelectShanheS5LuoyiAiOption(PendingDecision decision, ProgramSkillFrame frame,
        ProgramExecutionPlan plan, out PromptChoice choice)
    {
        choice = null!;
        var apply = plan.Instructions.Skip(frame.InstructionIndex).FirstOrDefault(e =>
            e.Op == SkillProgramEffectOp.ResolveShanheS5Luoyi && e.StateId == "apply");
        if (apply is null || decision.PlayerSeat != frame.OwnerSeat) return false;
        var skip = decision.Choices.SingleOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == ShanheS5LuoyiComposition.Replace);
        var normal = decision.Choices.SingleOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == ShanheS5LuoyiComposition.Normal);
        if (skip is null || normal is null) return false;

        var parent = ShanheS5LuoyiDrawPlan(frame);
        var frozen = ShanheS5LuoyiFrozen(frame, apply.SourceBind!);
        var revealed = ProgramEventHistory<ProgramCardsRevealedEvent>().Single(e =>
            e.FrameId == frame.Id && e.Bind == apply.SourceBind);
        var gained = revealed.Cards.Where(c => ProgramCardSetFilter.Matches(c.Kind, c.Suit, [],
            [SkillProgramCardCategory.Basic], [EquipmentSlot.Weapon], [CardKind.Duel])).ToArray();
        var owner = _players[frame.OwnerSeat];
        var normalCount = Math.Max(0, checked((parent.FrozenBaseDrawCount ?? GetTurnDrawCount(owner)) + parent.NormalDrawAdjustment));

        // The revealed faces and X are public. A known acquired attack can exploit the full
        // next-owner-turn bonus; an existing anonymous hand has a smaller attack opportunity
        // prior. An empty hand with no acquired attack cannot immediately exploit that bonus.
        // Neither estimate reads an unrevealed deck face or another player's hand.
        var knownAttack = gained.Any(c => IsSlashCard(c.Kind) || c.Kind == CardKind.Duel);
        var bonusOpportunity = knownAttack ? 11d : GetHand(owner).Count > 0 ? 6d : 0d;
        var skipValue = gained.Length * 8d + frozen.X * bonusOpportunity;
        var normalValue = normalCount * 8d;
        choice = skipValue > normalValue ? skip : normal;
        return true;
    }
}

internal static class ShanheS5LuoyiAi
{
    private static readonly double UnknownGainFraction = Enum.GetValues<Suit>().Average(suit =>
        ProgramCardSetFilter.PriorForSuit(suit, [], [SkillProgramCardCategory.Basic], [EquipmentSlot.Weapon], [CardKind.Duel]));

    internal static double UnknownChoiceScore(PlayerSkillContext owner, ProgramAiPublicContext context)
    {
        // This is used before activating/revealing, when the branch lookahead has no
        // actual faces. Match the shared type prior and compare against the real public
        // normal-draw budget. Existing hand count gives a larger combat opportunity;
        // an empty hand still has the unknown reveal's potential attacks.
        var hp = Math.Max(0, owner.Hp);
        var expectedGain = hp * UnknownGainFraction;
        var bonusOpportunity = owner.HandCount > 0 ? 11d : 6d;
        return expectedGain * 8d + hp * bonusOpportunity - Math.Max(0, context.NormalDrawCount) * 8d;
    }
}
