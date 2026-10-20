namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome UseProgramSelectedCardsAsGlobal(
        ProgramSkillFrame frame, ProgramMultiCardViewAsSelection selection)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[frame.OwnerSeat];
        if (active.TriggerId is not null || active.SelectedTargetSeats.Count != 0 &&
            !HasExactNextActualUseProgramSelection(active, ProgramInstructionResolver.Default.Resolve(active, _contentRegistry.GetSkill(active.SkillId).Program!)) ||
            selection.OutputKind != CardKind.ArrowBarrage ||
            selection.Cards.Count != 2 ||
            ViewAsRule(selection.Source) is { SameSuit: true } && selection.Cards[0].Suit != selection.Cards[1].Suit ||
            !CanUseGlobalCard(owner, CardKind.ArrowBarrage) ||
            !selection.Cards.All(card => GetHand(owner).Any(held => held.Id == card.Id)))
            throw new InvalidOperationException(
                "A global selected-card use requires two legal same-suit owner hand cards.");

        var targets = Enumerable.Range(1, _playerCount - 1)
            .Select(offset => _players[(owner.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                !IsCardTargetProhibited(player, CardKind.ArrowBarrage, PhysicalGroupSuit(owner, selection.Cards), PhysicalGroupColor(owner, selection.Cards)) &&
                !HasBeneficiarySuitShield(owner.Seat, player.Seat, PhysicalGroupSuit(owner, selection.Cards)))
            .Select(player => player.Seat)
            .ToArray();
        var adjusted = ApplyNextActualUseGlobalRemoval(active, targets, out var changedTargets);
        targets = changedTargets.ToArray();
        var representative = selection.Cards[0];
        var physicalIds = selection.Cards.Select(card => card.Id).ToArray();
        var resolutionId = BeginCardUse(representative, owner.Seat, targets,
            CardKind.ArrowBarrage, physicalCardIds: physicalIds,
            conversionSource: selection.Source);
        if (adjusted) UpdateLifecycleCardUse(resolutionId, use => use with { TargetsAdjusted = true });
        MoveCards(selection.Cards, CardLocation.Hand(owner.Seat), CardLocation.Processing,
            CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(resolutionId, frame.SkillId,
            selection.Source.BindingId, owner.Seat, Array.AsReadOnly(physicalIds),
            CardKind.ArrowBarrage, IsUse: true, targets));
        BeginJizhiOrNullificationWindow(resolutionId, representative, owner.Seat,
            targets, LegalActionKind.ArrowBarrage,
            requiredCardKind: CardKind.Dodge, playedCardKind: CardKind.ArrowBarrage);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
