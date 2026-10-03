namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome UseProgramSelectedCardsAsGlobal(
        ProgramSkillFrame frame, ProgramMultiCardViewAsSelection selection)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[frame.OwnerSeat];
        if (active.TriggerId is not null || active.SelectedTargetSeats.Count != 0 ||
            selection.OutputKind != CardKind.ArrowBarrage ||
            selection.Cards.Count != 2 ||
            selection.Cards[0].Suit != selection.Cards[1].Suit ||
            !CanUseGlobalCard(owner, CardKind.ArrowBarrage) ||
            !selection.Cards.All(card => GetHand(owner).Any(held => held.Id == card.Id)))
            throw new InvalidOperationException(
                "A global selected-card use requires two legal same-suit owner hand cards.");

        var targets = Enumerable.Range(1, _playerCount - 1)
            .Select(offset => _players[(owner.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                // The two cards share one suit, so the virtual Arrow Barrage
                // carries that suit into suit-based target shields.
                !IsCardTargetProhibited(player, CardKind.ArrowBarrage, selection.Cards[0].Suit, PhysicalGroupColor(owner, selection.Cards)) &&
                !HasBeneficiarySuitShield(owner.Seat, player.Seat, EffectiveSuit(owner, selection.Cards[0])))
            .Select(player => player.Seat)
            .ToArray();
        var representative = selection.Cards[0];
        var physicalIds = selection.Cards.Select(card => card.Id).ToArray();
        var resolutionId = BeginCardUse(representative, owner.Seat, targets,
            CardKind.ArrowBarrage, physicalCardIds: physicalIds,
            conversionSource: selection.Source);
        MoveCards(selection.Cards, CardLocation.Hand(owner.Seat), CardLocation.Processing,
            CardMoveReasons.Use);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(resolutionId, frame.SkillId,
            selection.Source.BindingId, owner.Seat, Array.AsReadOnly(physicalIds),
            CardKind.ArrowBarrage, IsUse: true, targets));
        BeginJizhiOrNullificationWindow(resolutionId, representative, owner.Seat,
            targets, LegalActionKind.ArrowBarrage,
            requiredCardKind: CardKind.Dodge, playedCardKind: CardKind.ArrowBarrage);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
