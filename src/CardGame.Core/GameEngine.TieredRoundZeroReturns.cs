namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TieredRoundZeroDyingRescueRide(int dyingIndex, DyingFrame dying)
    {
        if (dyingIndex < 0 || dyingIndex + 1 >= _resolutionStack.Count || ActiveDying?.FrameId != dying.Id ||
            _resolutionStack[dyingIndex] is not DyingFrame original || original != dying ||
            _resolutionStack[dyingIndex + 1] is not CardUseFrame rescue || !IsIssuedTieredRoundUse(rescue, true) ||
            rescue.TieredRoundConversionUse!.DyingFrameId != dying.Id || rescue.DyingResponse is not { } token ||
            token.ResolutionId != dying.Id || token.ResponderSeat != dying.ResponderSeat || rescue.SourceSeat != dying.ResponderSeat ||
            rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) || !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            rescue.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } action || action.ActorSeat != token.ResponderSeat ||
            !action.EffectiveDesignatedTargetSeats.SequenceEqual([dying.VictimSeat]) ||
            (rescue.CardKind == CardKind.Peach ? !token.UsedPeach || token.PeachCardId != 0 || token.UsedAlcohol || token.AlcoholCardId is not null :
                !token.UsedAlcohol || token.AlcoholCardId != 0 || token.UsedPeach || token.PeachCardId is not null || rescue.SourceSeat != dying.VictimSeat) ||
            token.UsedPeachPhysicalCardKind is not null) return false;
        for (var child = dyingIndex + 2; child < _resolutionStack.Count; child++)
            if (!ParticipantHandRescueObserverRide(_resolutionStack[child], _resolutionStack[child - 1], rescue)) return false;
        return true;
    }

    private bool HasTieredRoundZeroDyingRide(long dyingId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == dyingId && f is DyingFrame);
        return index >= 0 && TieredRoundZeroDyingRescueRide(index, (DyingFrame)_resolutionStack[index]);
    }

    private bool IsTieredRoundZeroRescueProgramDying() => ActiveDying is { } dying &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame && HasTieredRoundZeroDyingRide(dying.FrameId);

    private Card ReadTieredRoundUseAppearance(long ownerId, CardAppearanceReference appearance) =>
        appearance.Id == 0 && IsTieredRoundZeroUse(ownerId) && LifecycleCardUse(ownerId)!.CardKind == appearance.Kind &&
            appearance.Suit == Suit.None && appearance.Rank == 0
            ? TieredRoundZeroRepresentation(ownerId) : ReadCardAppearance(appearance);

    private bool IsTieredRoundZeroAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (!IsTieredRoundZeroUse(attack.ResolutionId) || LifecycleCardUse(attack.ResolutionId) is not { } use ||
            attack.Card is not null || attack.PhysicalCards.Count != 0 || attack.EffectiveCardKind != use.CardKind) return false;
        var remaining = processing;
        if (ActiveBorrowedSword is { ActiveAttack: { } forced } borrowed && SameAttackOwner(forced, attack))
        {
            if (use.SourceSeat != borrowed.WeaponOwnerSeat || !use.TargetSeats.SequenceEqual([borrowed.SlashTargetSeat]) ||
                use.Action?.ParentActionId != LifecycleCardUse(borrowed.ResolutionId)?.Action?.ActionId) return false;
            var parentIds = GetCardUsePhysicalCards(borrowed.ResolutionId).Select(c => c.Id).ToArray();
            if (parentIds.Any(id => processing.All(c => c.Id != id))) return false;
            remaining = processing.Where(c => !parentIds.Contains(c.Id)).ToArray();
        }
        else if (ActiveQinglongFollowup is { } blade && blade.OuterResolutionId != use.Id &&
            LifecycleCardUse(blade.OuterResolutionId) is { } original &&
            use.Action?.ParentActionId == original.Action?.ActionId &&
            CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().LastOrDefault() is
                { Used: true, SlashCardIds.Count: 0 } fact && fact.ResolutionId == original.Id &&
            fact.SourceSeat == use.SourceSeat && fact.TargetSeat == attack.TargetSeat && fact.EffectiveSlashKind == use.CardKind)
        {
            var exactParentIds = blade.NextAttack?.PhysicalCards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing)
                .Select(c => c.Id).ToArray() ?? [];
            if (exactParentIds.Any(id => processing.All(c => c.Id != id))) return false;
            remaining = processing.Where(c => !exactParentIds.Contains(c.Id)).ToArray();
        }
        if (use.CardKind == CardKind.FireAttack)
            return remaining.Count == 0 && MatchesTieredRoundZeroFireAttackAttack(attack);
        return remaining.Count == 0 &&
            ((IsSlashCard(use.CardKind) || use.CardKind == CardKind.Duel) && attack.SourceSeat == use.SourceSeat ||
                use.CardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage && ActiveGroupCard is { } group &&
                    group.ResolutionId == use.Id && group.SourceSeat == use.SourceSeat && group.DamageSourceSeat == attack.SourceSeat &&
                    group.CurrentAttack?.ResolutionId == use.Id);
    }

    private bool IsTieredRoundZeroFireAttackUse(long id) =>
        LifecycleCardUse(id) is { CardId: 0, CardKind: CardKind.FireAttack, PhysicalCardIds.Count: 0 } use &&
        IsIssuedTieredRoundUse(use, true);

    // The logical parent is an exact issued tier2 Use, not a physical card0.
    // The real reveal/discard fact proves that one of the mature payment
    // producers has reached this damage route. Its initial target remains in
    // the owning target list even after legitimate redirect/chain propagation
    // or sequential-target completion changes the attack target/cursor.
    private bool MatchesTieredRoundZeroFireAttackAttack(CardAttackHandle attack) =>
        IsTieredRoundZeroFireAttackUse(attack.ResolutionId) &&
        LifecycleCardUse(attack.ResolutionId) is { } use &&
        attack.Card is null && attack.PhysicalCards.Count == 0 &&
        attack.EffectiveCardKind == CardKind.FireAttack &&
        attack.SourceSeat == use.SourceSeat && attack.CardUserSeat == use.SourceSeat &&
        attack.ProgramSkillFrameId is null && attack.ProgramSkillCardUseFrameId is null &&
        attack.ProgramJudgmentFrameId is null && !attack.IsDelayedJudgmentDamage &&
        CompleteProgramEventHistory().OfType<FireAttackResolvedEvent>()
            .LastOrDefault(fact => fact.ResolutionId == use.Id) is
                { CausedDamage: true, MatchingDiscardCardId: > 0 } paid &&
        paid.SourceSeat == use.SourceSeat && use.TargetSeats.Contains(paid.TargetSeat);

    private bool IsTieredRoundZeroProcessingParent(long id, int cardId) => cardId == 0 && IsTieredRoundZeroUse(id);

    private bool MatchesTieredRoundZeroUseAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && LifecycleCardUse(id) is { } use && IsIssuedTieredRoundUse(use, true) &&
        TieredRoundActionsStructurallyMatch(use.Action, action) && action.Type == CardActionType.Use && action.EffectiveKind == use.CardKind &&
        action.PhysicalCards.Count == 0;

    // A real Borrowed Sword may retain its own physical material while its
    // weapon owner's child Slash is physical. Only the issued logical parent
    // removes the old requirement for a physical Borrowed Sword entity.
    private bool IsTieredRoundZeroBorrowedAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (ActiveBorrowedSword is not { ActiveAttack: { } child } borrowed || !SameAttackOwner(child, attack) ||
            !IsTieredRoundZeroUse(borrowed.ResolutionId) || LifecycleCardUse(borrowed.ResolutionId)?.CardKind != CardKind.BorrowedSword ||
            attack.Card is null || !IsSlashCard(attack.EffectiveCardKind ?? attack.Card.Kind)) return false;
        var costs = attack.PhysicalCards.Where(c => !IsCurrentUsePhysicalCardClaim(attack.ResolutionId, c.Id)).ToArray();
        if (costs.All(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing))
            return processing.Select(c => c.Id).Order().SequenceEqual(costs.Select(c => c.Id).Order());
        return LifecycleCardUse(attack.ResolutionId) is { Step: ResolutionFrameStep.Completed, Action: { } accepted } use &&
            accepted.PhysicalCards.Select(c => c.CardId).SequenceEqual(attack.PhysicalCards.Select(c => c.Id)) &&
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is
                { Continuation: ProgramCardContinuation.CompletedSlash } window && window.ParentFrameId == use.Id &&
            TieredRoundActionsStructurallyMatch(window.Action, accepted) && processing.Count == 0 && costs.All(c => _cardZones.GetLocation(c.Id).Zone is
                CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile);
    }

    // CardActionContext is a class; independent JSON fields reconstruct distinct
    // objects. Only this new receipt's return proof compares the complete frozen
    // value. Existing default/reference semantics remain unchanged.
    private static bool TieredRoundActionsStructurallyMatch(CardActionContext? left, CardActionContext right) =>
        left is not null && left.ActionId == right.ActionId && left.ParentActionId == right.ParentActionId &&
        left.Type == right.Type && left.ActorSeat == right.ActorSeat && left.ProviderSeat == right.ProviderSeat &&
        left.RequesterSeat == right.RequesterSeat && left.ResponderSeat == right.ResponderSeat && left.OpponentSeat == right.OpponentSeat &&
        left.EffectiveKind == right.EffectiveKind && left.EffectiveSuit == right.EffectiveSuit && left.EffectiveRank == right.EffectiveRank &&
        left.EffectiveIsRed == right.EffectiveIsRed && left.FactionOrigin == right.FactionOrigin &&
        left.TargetSeats.SequenceEqual(right.TargetSeats) &&
        (left.DesignatedTargetSeats is null ? right.DesignatedTargetSeats is null :
            right.DesignatedTargetSeats is not null && left.DesignatedTargetSeats.SequenceEqual(right.DesignatedTargetSeats)) &&
        left.PhysicalCards.SequenceEqual(right.PhysicalCards) && left.ConversionChain.SequenceEqual(right.ConversionChain);

    private bool SkipTieredRoundZeroFinishedMovement(long id, Card card)
    {
        if (card.Id != 0 || !IsTieredRoundZeroUse(id)) return false;
        if (card.Kind != LifecycleCardUse(id)!.CardKind) throw new InvalidOperationException("The logical completed card changed its issued card name.");
        return true; // No physical entity was issued; there is no cleanup move.
    }

    private void AssertCappedConversionBenefits()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.CappedConversionBenefit is not null))
            if (!ValidCappedConversionBenefit(frame)) throw new InvalidOperationException("A conversion benefit lost its exact draw and after-damage issuance.");
    }

    private bool IsCappedConversionAwaitedMovement(ProgramSkillFrame frame, ProgramMovementContinuation pending) =>
        frame.CappedConversionBenefit is { AwaitingMovement: true } && pending.SubjectSeat == frame.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidCappedConversionBenefit(frame);
}
