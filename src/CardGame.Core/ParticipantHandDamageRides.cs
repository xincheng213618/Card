namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? GetPaidHandRepaymentRoot(long damageWindowId)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame frame || frame.HandRepayment is not { Paid: true } receipt ||
                frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context || context.ParentFrameId != damageWindowId ||
                frame.InstructionIndex != receipt.InstructionIndex || ParticipantHandPaused(frame).Op != SkillProgramEffectOp.RequestHandBySuitsOrLoseHp)
                continue;
            var child = _resolutionStack[index + 1];
            var valid = child switch
            {
                CardsMovedTriggerWindowFrame move when receipt.PaidCardId is { } card => move.ResumeProgramFrameId == frame.Id &&
                    move.Batch.ParentFrameId == frame.Id && move.Batch.AwaitingProgramFrameId is null &&
                    move.Batch.OriginSkillId == frame.SkillId && move.Batch.OriginSkillInstanceId == frame.SkillInstanceId &&
                    move.Batch.Movements.Any(m => m.CardId == card && m.From == CardLocation.Hand(receipt.SourceSeat) &&
                        m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == $"skill-program.{frame.SkillId}.hand-repayment"),
                HpChangedTriggerWindowFrame hp when receipt.LostHp => hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.Program &&
                    hp.Change.ParentFrameId == frame.Id && hp.Change.TargetSeat == receipt.SourceSeat,
                DyingFrame dying when receipt.LostHp => dying.ParentFrameId == frame.Id && dying.VictimSeat == receipt.SourceSeat,
                _ => false
            };
            if (valid) return frame;
        }
        return null;
    }

    private bool HasPaidHandRepaymentObserver(long damageWindowId) => GetPaidHandRepaymentRoot(damageWindowId) is not null;

    private static bool ParticipantHandDyingRide(ResolutionFrame child, ResolutionFrame parent) =>
        child is DyingFrame dying && parent is ProgramSkillFrame program && dying.ParentFrameId == program.Id ||
        child is ProgramSkillFrame { WindowContext: { } context } response && parent is DyingFrame source &&
        context.ParentFrameId == source.Id &&
        (context.Window == SkillProgramTriggerWindow.SelfDyingResponse && response.OwnerSeat == source.VictimSeat ||
         context.Window == SkillProgramTriggerWindow.DyingResponse && response.OwnerSeat == source.ResponderSeat);

    private static bool ParticipantHandDyingEntryRide(ResolutionFrame child, ResolutionFrame parent, DyingFrame dying) =>
        child is ProgramLifecycleTriggerWindowFrame entry && parent.Id == dying.Id && parent is DyingFrame &&
        entry.Window == SkillProgramTriggerWindow.DyingEntering && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
        entry.ResumeDyingFrameId == dying.Id && entry.OwnerSeat == dying.VictimSeat ||
        child is ProgramSkillFrame { WindowContext: { Window: SkillProgramTriggerWindow.DyingEntering } context } program &&
        parent is ProgramLifecycleTriggerWindowFrame window && context.ParentFrameId == window.Id &&
        window.Window == SkillProgramTriggerWindow.DyingEntering && window.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
        window.ResumeDyingFrameId == dying.Id && window.OwnerSeat == dying.VictimSeat &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        window.Candidates[window.CandidateIndex] is var candidate && candidate.OwnerSeat == program.OwnerSeat &&
        candidate.SkillId == program.SkillId && candidate.BindingId == program.TriggerId && candidate.SkillInstanceId == program.SkillInstanceId;

    private bool IsPaidHandRepaymentRescueRide(int dyingIndex, DyingFrame dying)
    {
        var useIndex = dyingIndex + 1;
        if (useIndex >= _resolutionStack.Count || _resolutionStack[useIndex] is not CardUseFrame rescue ||
            rescue.DyingResponse is not { } response || response.ResolutionId != dying.Id || response.ResponderSeat != dying.ResponderSeat ||
            rescue.SourceSeat != response.ResponderSeat || rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
            response.UsedPeach != (rescue.CardKind == CardKind.Peach) || response.UsedAlcohol != (rescue.CardKind == CardKind.Alcohol) ||
            (response.UsedPeach ? response.PeachCardId != rescue.CardId || response.AlcoholCardId is not null
                : response.AlcoholCardId != rescue.CardId || response.PeachCardId is not null || rescue.SourceSeat != dying.VictimSeat) ||
            !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) || rescue.Action is not { Type: CardActionType.Use } action ||
            action.EffectiveKind != rescue.CardKind || action.ActorSeat != rescue.SourceSeat || action.ProviderSeat != rescue.SourceSeat ||
            !action.EffectiveDesignatedTargetSeats.SequenceEqual(rescue.TargetSeats) || action.PhysicalCards.Count == 0 ||
            action.PhysicalCards[0].CardId != rescue.CardId ||
            !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(rescue.PhysicalCardIds ?? [rescue.CardId]) ||
            action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != action.PhysicalCards.Count ||
            action.PhysicalCards.Any(cost => cost.From.OwnerSeat != rescue.SourceSeat ||
                !_cardZones.CardsAt(_cardZones.GetLocation(cost.CardId)).Any(c => c.Id == cost.CardId && c.Kind == cost.CardKind)))
            return false;
        if (action.ConversionChain.Count == 0)
        {
            if (action.PhysicalCards is not [var native] || native.CardKind != rescue.CardKind ||
                native.From.Zone is not (CardZoneKind.Hand or CardZoneKind.WoodenOxGrain) || response.UsedPeachPhysicalCardKind is not null) return false;
        }
        else
        {
            if (action.ConversionChain is not [var conversion] || conversion.OwnerSeat != rescue.SourceSeat ||
                string.IsNullOrWhiteSpace(conversion.SkillId) || string.IsNullOrWhiteSpace(conversion.BindingId) ||
                string.IsNullOrWhiteSpace(conversion.SkillInstanceId) ||
                _contentRegistry.Skills.GetValueOrDefault(conversion.SkillId)?.Program?.ViewAs.SingleOrDefault(r => r.Id == conversion.BindingId) is not { } rule ||
                rule.OutputKind != rescue.CardKind || !rule.ForResponse && !(rule.UseOnly && rule.ForPlay) ||
                rule.InputCount != action.PhysicalCards.Count || action.PhysicalCards.Any(cost => !rule.SourceZones.Contains(cost.From.Zone))) return false;
            // Validate frozen provenance, never the giver's post-payment skill availability, condition or rewritten suits.
            if (response.UsedPeach && response.UsedPeachPhysicalCardKind != (rule.InputCount > 1 || action.PhysicalCards[0].CardKind == CardKind.Peach
                ? (CardKind?)null : action.PhysicalCards[0].CardKind)) return false;
        }
        var index = _resolutionStack.Count - 1;
        while (index > useIndex)
        {
            var frame = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (!ParticipantHandRescueObserverRide(frame, parent, rescue)) return false;
            index--;
        }
        return index == useIndex;
    }

    private static bool ParticipantHandRescueCardWindowRide(ResolutionFrame child, ResolutionFrame parent, CardUseFrame rescue)
    {
        bool WindowMatches(ProgramCardTriggerWindowFrame window) => rescue.Action is { } action &&
            window.Action.ActionId == action.ActionId && window.Action.Type == CardActionType.Use &&
            window.Action.ActorSeat == action.ActorSeat && window.Action.ProviderSeat == action.ProviderSeat &&
            window.Action.EffectiveKind == action.EffectiveKind && window.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) &&
            window.Action.ConversionChain.SequenceEqual(action.ConversionChain) && window.CompletedResponseReturn is null &&
            (window.Continuation is ProgramCardContinuation.CommittedSimpleCard or ProgramCardContinuation.FinalizedSimpleCard
                ? window.SimpleContinuation is { Effect: SimpleCardUseEffect.Recovery } simple && simple.CardId == rescue.CardId
                : window.Continuation == ProgramCardContinuation.CompletedCard && window.SimpleContinuation is null);
        if (child is ProgramCardTriggerWindowFrame window)
            return window.ParentFrameId == parent.Id && WindowMatches(window) &&
                (parent.Id == rescue.Id && parent is CardUseFrame || parent is ProgramCardTriggerWindowFrame beneath && WindowMatches(beneath));
        if (child is not ProgramSkillFrame { WindowContext: { CardUse: { } cardUse } context } program ||
            parent is not ProgramCardTriggerWindowFrame owner || !WindowMatches(owner) || context.ParentFrameId != owner.Id ||
            cardUse.CardActionId != owner.Action.ActionId || cardUse.ParentCardUseFrameId != owner.ParentFrameId ||
            owner.CandidateIndex < 0 || owner.CandidateIndex >= owner.Candidates.Count) return false;
        var candidate = owner.Candidates[owner.CandidateIndex];
        var expectedWindow = owner.Continuation switch
        { ProgramCardContinuation.CommittedSimpleCard => SkillProgramTriggerWindow.CardUseCommitted,
          ProgramCardContinuation.FinalizedSimpleCard => SkillProgramTriggerWindow.CardUseTargetsFinalized,
          _ => SkillProgramTriggerWindow.CardUseCompleted };
        return context.Window == expectedWindow && candidate.OwnerSeat == program.OwnerSeat && candidate.SkillId == program.SkillId &&
            candidate.TriggerId == program.TriggerId && candidate.SkillInstanceId == program.SkillInstanceId && candidate.GameplayHash == program.GameplayHash;
    }

    private static bool ParticipantHandRescueObserverRide(ResolutionFrame frame, ResolutionFrame parent, CardUseFrame rescue)
    {
        if (frame is HpChangedTriggerWindowFrame hp && parent.Id == rescue.Id &&
            (hp.Change.ParentFrameId != rescue.Id || hp.ResumeFrameId != rescue.Id || hp.Continuation != PostEventContinuation.CardUse)) return false;
        return DamageFrameRidesOn(frame, parent) || DamageObserverRidesOn(frame, parent) ||
            RecoveryReplacementFrameRidesOn(frame, parent) || ParticipantHandRescueCardWindowRide(frame, parent, rescue);
    }

    private bool IsPaidHandRepaymentProgramDying()
    {
        if (ActiveDamageTrigger is not { } trigger || ActiveDying is not { ResumesProgramSkill: true } dying ||
            GetPaidHandRepaymentRoot(trigger.Id) is not { } root ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == dying.FrameId) is not { } child || child.ParentFrameId != dying.ParentFrameId)
            return false;
        var top = DamageCursorEffectiveTop(includeNestedObservers: true);
        if (top is null) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == top.Id);
        var paidSourceEntry = root.HandRepayment is { Paid: true, LostHp: true, PaidCardId: null } receipt &&
            child.Continuation == DyingContinuationKind.ProgramSkill && child.ParentFrameId == root.Id && child.VictimSeat == receipt.SourceSeat;
        var dyingIndex = _resolutionStack.FindIndex(f => f.Id == child.Id);
        var rescueRide = paidSourceEntry && (IsPaidHandRepaymentRescueRide(dyingIndex, child) || IsPaidHandRepaymentProgramAlcoholRide(dyingIndex, child));
        if (!(top is DyingFrame topDying && topDying.Id == child.Id ||
              ParticipantHandDyingRide(top, child) ||
              paidSourceEntry && index >= 1 && ParticipantHandDyingEntryRide(top, _resolutionStack[index - 1], child) || rescueRide)) return false;
        if (rescueRide) index = dyingIndex;
        while (index >= 1 && _resolutionStack[index].Id != root.Id)
        {
            var frame = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (!DamageFrameRidesOn(frame, parent) && !DamageObserverRidesOn(frame, parent) && !ParticipantHandDyingRide(frame, parent) &&
                !(paidSourceEntry && ParticipantHandDyingEntryRide(frame, parent, child))) return false;
            index--;
        }
        return index >= 0 && _resolutionStack[index].Id == root.Id;
    }
}
