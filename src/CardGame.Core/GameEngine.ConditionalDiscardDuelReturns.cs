namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidConditionalDuelPayments(ProgramSkillFrame f)
    {
        if (!ConditionalDuelParentMatches(f) || f.ConditionalDiscardDuel is not { } d || !Enum.IsDefined(d.Stage)) return false;
        bool Paid(ConditionalDiscardDuelPayment? p, bool owner)
        {
            if (p is null) return false;
            var seat = owner ? f.OwnerSeat : d.TargetSeat;
            var reason = owner ? ConditionalDuelOwnerReason : ConditionalDuelTargetReason;
            var moved = _cardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter).ToArray();
            return p.PayerSeat == seat && p.CardId > 0 && p.From.OwnerSeat == seat &&
                p.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && p.SequenceBefore >= 0 && p.SequenceAfter > p.SequenceBefore &&
                moved is [var actual] && actual.CardId == p.CardId && actual.CardKind == p.PrintedKind && actual.From == p.From &&
                actual.Reason.Value == reason && actual.TurnNumber == d.TurnNumber &&
                (actual.To == CardLocation.DiscardPile || p.From.Zone == CardZoneKind.Equipment && actual.To == CardLocation.OutsideGame && GetAttackCard(p.CardId).IsGeneralWeapon) &&
                (!owner || IsSlashCard(p.PrintedKind) && !GetAttackCard(p.CardId).IsGeneralWeapon) &&
                CompleteProgramEventHistory().OfType<ConditionalDiscardDuelPaidEvent>().Count(e => e.ProgramFrameId == f.Id &&
                    e.PayerSeat == seat && e.OwnerCost == owner && e.SequenceBefore == p.SequenceBefore && e.SequenceAfter == p.SequenceAfter) == 1;
        }
        if (d.Stage == ConditionalDiscardDuelStage.OwnerChoice) return d.OwnerPayment is null && d.TargetPayment is null &&
            d.CardUseFrameId is null && d.OwnerHpAtIssue is null && d.TargetHpAtIssue is null;
        if (!Paid(d.OwnerPayment, true)) return false;
        if (d.Stage is ConditionalDiscardDuelStage.OwnerChildren or ConditionalDiscardDuelStage.TargetChoice)
            return d.TargetPayment is null && d.CardUseFrameId is null && d.OwnerHpAtIssue is null && d.TargetHpAtIssue is null;
        return Paid(d.TargetPayment, false) && (d.Stage == ConditionalDiscardDuelStage.TargetChildren
            ? d.CardUseFrameId is null && d.OwnerHpAtIssue is null && d.TargetHpAtIssue is null :
            d.CardUseFrameId is > 0 && d.OwnerHpAtIssue is { } ownerHp && d.TargetHpAtIssue >= ownerHp &&
            !IsSlashCard(d.TargetPayment!.PrintedKind));
    }
    private bool MatchesConditionalDuelOrigin(ProgramSkillFrame f, ConditionalDiscardDuelOrigin o) =>
        ValidConditionalDuelPayments(f) && f.ConditionalDiscardDuel is { Stage: ConditionalDiscardDuelStage.DuelIssued } d &&
        f.Id == o.ParentProgramFrameId && o.InstructionIndex == d.InstructionIndex && d.CardUseFrameId == o.CardUseFrameId &&
        o.Source == d.Source && o.GameplayHash == d.GameplayHash && o.TurnNumber == d.TurnNumber && o.TurnOwnerSeat == d.TurnOwnerSeat &&
        o.InitialActorSeat == f.OwnerSeat && o.InitialTargetSeat == d.TargetSeat &&
        o.OwnerHpAtIssue == d.OwnerHpAtIssue && o.TargetHpAtIssue == d.TargetHpAtIssue &&
        CompleteProgramEventHistory().OfType<ConditionalDiscardDuelIssuedEvent>().Count(e => e.Origin == (o with { AttackStarted = false })) == 1;
    private bool IsConditionalDiscardDuelUse(long id) => LifecycleCardUse(id) is
        { ConditionalDiscardDuelOrigin: not null, CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0 };
    private void ReturnConditionalDiscardDuel(CardUseFrame use)
    {
        if (use.ConditionalDiscardDuelOrigin is not { AttackStarted: false } origin) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || use.Id != origin.CardUseFrameId || !MatchesConditionalDuelOrigin(f, origin))
            throw new InvalidOperationException("Conditional Duel lost its exact no-attack typed return.");
        FinishConditionalDiscardDuel(f, true);
    }
    private void CompleteConditionalDiscardDuelAttackReturn(AttackCompletionReceipt completion)
    {
        if (completion.ConditionalDiscardDuelReturn is not { AttackStarted: true } origin || completion.ResolutionId != origin.CardUseFrameId ||
            completion.ProgramFrameId != origin.ParentProgramFrameId || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f ||
            f.Id != origin.ParentProgramFrameId || !MatchesConditionalDuelOrigin(f, origin))
            throw new InvalidOperationException("Conditional Duel lost its exact completed attack return.");
        FinishConditionalDiscardDuel(f, true);
    }
    private void AssertConditionalDiscardDuels()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.ConditionalDiscardDuel is not null))
        {
            if (!ValidConditionalDuelPayments(f)) throw new InvalidOperationException("Conditional Duel has an invalid cost or activation receipt.");
            var d = f.ConditionalDiscardDuel!;
            if (d.Stage is ConditionalDiscardDuelStage.OwnerChoice or ConditionalDiscardDuelStage.TargetChoice)
            {
                if (_resolutionStack.LastOrDefault()?.Id == f.Id && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                    prompt.PlayerSeat != (d.Stage == ConditionalDiscardDuelStage.OwnerChoice ? f.OwnerSeat : d.TargetSeat) ||
                    !prompt.Choices.Select(c => c.Id).SequenceEqual(ConditionalDuelChoices(f).Select(c => c.Id))))
                    throw new InvalidOperationException("Conditional Duel lost its exact own-card private payment prompt.");
            }
            if (d.Stage == ConditionalDiscardDuelStage.DuelIssued && !_resolutionStack.OfType<CardUseFrame>().Any(u =>
                u.Id == d.CardUseFrameId && u.ConditionalDiscardDuelOrigin is not null))
                throw new InvalidOperationException("Conditional Duel lost its issued use.");
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.ConditionalDiscardDuelOrigin is not null))
        {
            var o = use.ConditionalDiscardDuelOrigin!; var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame f || !MatchesConditionalDuelOrigin(f, o) ||
                use.Id != o.CardUseFrameId || !IsConditionalDiscardDuelUse(use.Id) ||
                use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                    EffectiveSuit: Suit.None, EffectiveIsRed: false } || use.SelectedActorDuelOrigin is not null ||
                use.DualColorDuelOrigin is not null || use.DamageTargetDuelOrigin is not null)
                throw new InvalidOperationException("Conditional Duel requires its exact paid parent and real zero-entity action.");
        }
    }
}
