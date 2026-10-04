namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This check runs at the exposed command boundary, after actual material
    // payment. It never re-evaluates the skill's current enabled state: the
    // accepted Use keeps its frozen issuance even if a paid child removes it.
    private void AssertGrantedEntityDistancePolicies()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
            if (use.GrantedEntityDistance is not null && !IsExactGrantedEntityDistancePolicy(use))
                throw new InvalidOperationException("A granted-entity distance policy lost its exact phase, issuance or actual material payment.");
    }

    private bool IsExactGrantedEntityDistancePolicy(CardUseFrame use)
    {
        if (use.GrantedEntityDistance is not { } policy || policy.CardUseFrameId != use.Id ||
            use.Action is not { Type: CardActionType.Use } action || action.ActionId != policy.CardActionId ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != action.ActorSeat ||
            action.ActorSeat != policy.Source.OwnerSeat || action.EffectiveKind != use.CardKind ||
            !IsSlashCard(action.EffectiveKind) || use.CardId != policy.CardId ||
            action.PhysicalCards is not [var cost] || cost.CardId != policy.CardId ||
            use.PhysicalCardIds is not [var physicalId] || physicalId != policy.CardId ||
            !IsProvenanceOwnedZone(cost.From, action.ActorSeat) ||
            _programPhaseSchedule is not { Phase: TurnPhase.Play } schedule ||
            schedule.Frame.Id != policy.PhaseProducerFrameId || schedule.Frame.OwnerSeat != action.ActorSeat ||
            schedule.Frame.IssuedEntityPhase is not { Stage: GrantedEntityPhaseStage.Active } phase ||
            phase.Source != policy.Source || phase.CardId != policy.CardId || phase.ClaimMovementSequence is not { } claimSequence ||
            phase.PhaseInstanceId != policy.PhaseInstanceId || phase.TurnNumber != _turnNumber ||
            _phase != TurnPhase.Play || _currentSeat != action.ActorSeat ||
            _cardUseDebitPhaseInstanceId != policy.PhaseInstanceId)
            return false;

        AssertGrantedEntityPhase(schedule.Frame);
        var history = CompleteProgramEventHistory().ToArray();
        if (!history.OfType<GrantedEntityDistanceUseIssuedEvent>().Any(e => e.Policy == policy) ||
            !history.OfType<GrantedEntityPhaseStartedEvent>().Any(e => e.FrameId == policy.PhaseProducerFrameId &&
                e.Source == policy.Source && e.TurnNumber == phase.TurnNumber && e.PhaseInstanceId == policy.PhaseInstanceId) ||
            !history.OfType<GrantedPhaseSlashClaimedEvent>().Any(e => e.PhaseProducerFrameId == policy.PhaseProducerFrameId &&
                e.Source == policy.Source && e.PhaseInstanceId == policy.PhaseInstanceId &&
                e.CardId == policy.CardId && e.MovementSequence == claimSequence) ||
            history.OfType<GrantedEntityPhaseEndedEvent>().Any(e => e.FrameId == policy.PhaseProducerFrameId &&
                e.Source == policy.Source && e.TurnNumber == phase.TurnNumber && e.PhaseInstanceId == policy.PhaseInstanceId) ||
            !history.OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id && e.SourceSeat == use.SourceSeat &&
                e.CardId == use.CardId && e.CardKind == use.CardKind))
            return false;

        var claim = _cardMovements.SingleOrDefault(m => m.Sequence == claimSequence);
        if (claim is null || claim.CardId != policy.CardId || claim.To != CardLocation.Hand(action.ActorSeat) ||
            claim.Reason.Value != "program.granted-phase-slash.claim" || !IsSlashCard(claim.CardKind) ||
            claim.CardKind != cost.CardKind || claim.From.Zone is not (CardZoneKind.DrawPile or CardZoneKind.DiscardPile))
            return false;
        var payment = _cardMovements.FirstOrDefault(m => m.CardId == policy.CardId && m.Sequence > claimSequence &&
            !IsProvenanceOwnedZone(m.To, action.ActorSeat));
        if (payment is null || payment.From != cost.From || payment.To != CardLocation.Processing ||
            payment.CardKind != cost.CardKind)
            return false;
        if (payment.Reason == CardMoveReasons.Use) return true;

        // A successful declaration pays before its actual Use is accepted.
        // Accept only that use's already-claimed receipt and original Hand cost;
        // an unrelated Processing entry cannot resurrect an expired entity.
        return payment.Reason.Value == "conversion.declaration.pay" && cost.From == CardLocation.Hand(action.ActorSeat) &&
            use.AcceptedDeclarationPayment is { Claimed: true } declaration && declaration.OwnerFrameId == use.Id &&
            declaration.ProviderSeat == action.ProviderSeat && declaration.ActorSeat == action.ActorSeat &&
            declaration.Cost == cost && declaration.DeclaredKind == action.EffectiveKind &&
            declaration.Source.OwnerSeat == action.ActorSeat && action.ConversionChain.Contains(declaration.Source) &&
            history.OfType<CardDeclarationCommittedEvent>().Any(e => e.DeclarationId == declaration.DeclarationId &&
                e.OwnerSeat == action.ProviderSeat && e.ActorSeat == action.ActorSeat && e.DeclaredKind == action.EffectiveKind);
    }
}
