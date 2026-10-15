namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.CardBeforeDiscard, 20,
    SkillProgramEffectOp.DiscardMaximumAndPunishHigherHand)]
internal sealed class FriendMaximumSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionCardState>();
        return state.UniqueMaximumHandCardId(context.OwnerSeat) == card.Id &&
            !state.IsSelfDiscardForbidden(context.OwnerSeat, card, CardLocation.Hand(context.OwnerSeat));
    }
}
