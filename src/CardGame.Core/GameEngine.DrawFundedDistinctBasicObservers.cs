namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DrawFundedDistinctBasicFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) => ride is DrawFundedDistinctBasicFrame frame &&
        frame.Payment.RequestFrameId == beneath.Id && frame.Payment.ParentFrameId is { } parent && _resolutionStack.Any(f => f.Id == parent) &&
        frame.OriginalDecision.PlayerSeat == frame.Payment.Source.OwnerSeat && frame.OriginalDecision.PromptId == frame.Payment.OriginalPromptId &&
        frame.OriginalDecision.Revision == frame.Payment.OriginalRevision && ValidDrawFundedDistinctBasicPayment(frame.Payment);

    private bool DrawFundedDistinctBasicInitialChild(DrawFundedDistinctBasicFrame frame, ResolutionFrame child) => frame.ActiveChildFrameId == child.Id && child switch
    {
        CardsMovedTriggerWindowFrame move => move.ResumeDrawFundedDistinctBasicFrameId == frame.Id && move.Batch.ParentFrameId == frame.Id &&
            move.Batch.AwaitingProgramFrameId is null && move.ResumeProgramFrameId is null && move.Batch.Movements.Count > 0 &&
            move.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > frame.Payment.SequenceBefore && m.Sequence <= frame.Payment.SequenceAfter &&
                (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.Payment.Source.OwnerSeat) && m.Reason.Value == DrawFundedBasicDrawReason ||
                 m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle)),
        HpChangedTriggerWindowFrame hp => IsDrawFundedDistinctBasicHpParent(frame, hp),
        RecoveryReplacementFrame recovery => IsDrawFundedDistinctBasicRecoveryParent(frame, recovery),
        _ => false
    };

    private bool DrawFundedDistinctBasicPaidSuffix(int paidIndex)
    {
        if (_resolutionStack[paidIndex] is not DrawFundedDistinctBasicFrame paid || !ValidDrawFundedDistinctBasicPayment(paid.Payment)) return false;
        if (paidIndex == _resolutionStack.Count - 1) return paid.ActiveChildFrameId is null;
        if (!DrawFundedDistinctBasicInitialChild(paid, _resolutionStack[paidIndex + 1])) return false;
        for (var dyingIndex = paidIndex + 2; dyingIndex < _resolutionStack.Count; dyingIndex++)
            if (_resolutionStack[dyingIndex] is DyingFrame dying && DrawFundedDistinctBasicLegacyAlcoholRide(paid, dyingIndex, dying)) return true;
        for (var index = paidIndex + 2; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (parent is DyingFrame dying && (IsPaidHandRepaymentRescueRide(index - 1, dying) || IsPaidHandRepaymentProgramAlcoholRide(index - 1, dying) ||
                PolicyCounterspellVirtualAlcoholRide(index - 1, dying) || PaidObserverDamageVirtualAlcoholRide(index - 1, dying) ||
                TieredRoundZeroDyingRescueRide(index - 1, dying) || DrawFundedDistinctBasicDyingRescueRide(index - 1, dying))) return true;
            if (HalfHandPaidDamageObserverEdge(index) || DamageFrameRidesOn(child, parent) || DamageObserverRidesOn(child, parent) ||
                RecoveryReplacementFrameRidesOn(child, parent) || ParticipantHandDyingRide(child, parent) ||
                parent is DyingFrame entry && ParticipantHandDyingEntryRide(child, parent, entry) ||
                index >= 2 && _resolutionStack[index - 2] is DyingFrame originalEntry && ParticipantHandDyingEntryRide(child, parent, originalEntry)) continue;
            return false;
        }
        return true;
    }

    private DrawFundedDistinctBasicFrame? DrawFundedDistinctBasicObserverRoot(long? requestedId = null)
    {
        for (var index = 0; index < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is DrawFundedDistinctBasicFrame paid && paid.Stage == DrawFundedDistinctBasicStage.DrawChildren &&
                (requestedId is null || paid.Payment.ParentFrameId == requestedId || _resolutionStack.Skip(index + 1).Any(f => f.Id == requestedId)) &&
                DrawFundedDistinctBasicPaidSuffix(index)) return paid;
        return null;
    }
    private bool HasDrawFundedDistinctBasicObserver(long cardUseId) => DrawFundedDistinctBasicObserverRoot(cardUseId) is not null;
    private bool HasDrawFundedDistinctBasicDamageObserver(long damageId) => DrawFundedDistinctBasicObserverRoot(damageId) is not null;
    private bool IsDrawFundedDistinctBasicProgramDying() => ActiveDying is { } dying &&
        (DrawFundedDistinctBasicObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id) ||
         _resolutionStack.FindIndex(f => f.Id == dying.FrameId) is var index && index >= 0 && DrawFundedDistinctBasicDyingRescueRide(index, (DyingFrame)_resolutionStack[index]));

    private bool AllowsDrawFundedDistinctBasicNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (_resolutionStack.LastOrDefault()?.Id != observer.Id || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) || DrawFundedDistinctBasicObserverRoot() is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            !sourceLess && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    private PendingDecision? DrawFundedDistinctBasicInvariantDecision(PendingDecision? nativeDecision)
    {
        if (_resolutionStack.LastOrDefault() is DrawFundedDistinctBasicFrame paid)
            return DrawFundedDistinctBasicNativeDecision(RequestedDeckBasicNativeDecision(paid.OriginalDecision));
        return nativeDecision is { } prompt ? DrawFundedDistinctBasicNativeDecision(prompt) : null;
    }
    private ResolutionFrame? DrawFundedDistinctBasicInvariantTop(ResolutionFrame? nativeTop) => _resolutionStack.LastOrDefault() is DrawFundedDistinctBasicFrame paid
        ? _resolutionStack.SingleOrDefault(f => f.Id == paid.Payment.RequestFrameId) : nativeTop;

    private void AssertDrawFundedDistinctBasicFramesAndUses()
    {
        foreach (var paid in _resolutionStack.OfType<DrawFundedDistinctBasicFrame>())
        {
            var p = paid.Payment; var index = _resolutionStack.FindIndex(f => f.Id == paid.Id);
            if (p.PaymentFrameId != paid.Id || paid.Stage != DrawFundedDistinctBasicStage.DrawChildren || !ValidDrawFundedDistinctBasicPayment(p) ||
                paid.OriginalDecision.PlayerSeat != p.Source.OwnerSeat || paid.OriginalDecision.PromptId != p.OriginalPromptId || paid.OriginalDecision.Revision != p.OriginalRevision ||
                paid.TargetSeats.Count == 0 && IsSlashCard(p.EffectiveKind) || paid.TargetSeats.Distinct().Count() != paid.TargetSeats.Count || paid.TargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
                p.Intent != DrawFundedDistinctBasicIntent.Play && (index <= 0 || !DrawFundedDistinctBasicFrameRidesOn(paid, _resolutionStack[index - 1])) ||
                p.Intent == DrawFundedDistinctBasicIntent.Play && index != 0 || !DrawFundedDistinctBasicPaidSuffix(index) ||
                CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicIssuedEvent>().Any(e => e.PaymentFrameId == p.PaymentFrameId))
                throw new InvalidOperationException("A paid basic-card attempt lost its owning frame, frozen original decision or exact child subtree.");
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        {
            if (use.DrawFundedDistinctBasicUse is not null && !IsIssuedDrawFundedDistinctBasicUse(use))
                throw new InvalidOperationException("A paid basic Use lost its immutable issuance and unique native draw.");
            if (use.DrawFundedDistinctBasicResponse is not null) RequireDrawFundedDistinctBasicResponseAction(use);
            if (use.Action?.ConversionChain.Any(s => ViewAsRule(s)?.DrawFundedDistinctBasic is not null) == true && use.DrawFundedDistinctBasicUse is null)
                throw new InvalidOperationException("A new-policy Use cannot disguise a deleted owning receipt as a legacy zero-card action.");
        }
        foreach (var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Where(w => w.Action.Type == CardActionType.Response &&
            w.Action.ConversionChain.Any(s => ViewAsRule(s)?.DrawFundedDistinctBasic is not null)))
            if (LifecycleCardUse(window.ParentFrameId) is not { DrawFundedDistinctBasicResponse: not null } owner ||
                !TieredRoundActionsStructurallyMatch(RequireDrawFundedDistinctBasicResponseAction(owner), window.Action))
                throw new InvalidOperationException("A paid Dodge observer lost its exact original accepted response.");
    }
    // Only this family's already-paid draw subtree may carry the normalized
    // legacy virtual Alcohol's direct HP return. No legacy matcher is widened.
    private bool DrawFundedDistinctBasicLegacyAlcoholRide(DrawFundedDistinctBasicFrame paid, int dyingIndex, DyingFrame dying)
    {
        var paidIndex = _resolutionStack.FindIndex(frame => frame.Id == paid.Id);
        if (paidIndex < 0 || paidIndex + 1 >= dyingIndex || dyingIndex + 3 >= _resolutionStack.Count ||
            !ValidDrawFundedDistinctBasicPayment(paid.Payment) || !DrawFundedDistinctBasicInitialChild(paid, _resolutionStack[paidIndex + 1]) ||
            _resolutionStack[dyingIndex].Id != dying.Id || ActiveDying?.FrameId != dying.Id ||
            dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            _resolutionStack[dyingIndex - 1] is not ProgramSkillFrame losing || losing.Id != dying.ParentFrameId ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame program ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != dying.VictimSeat || context.SourceSeat is not null ||
            context.DamageFrameId is not null || context.OwnerSeat != program.OwnerSeat || context.OccurrenceIndex != 0 ||
            program.OwnerSeat != dying.VictimSeat || program.OwnerSeat != dying.ResponderSeat ||
            string.IsNullOrWhiteSpace(program.TriggerId) || string.IsNullOrWhiteSpace(program.SkillInstanceId) ||
            program.ActivationId != program.TriggerId || program.InstructionIndex < 1 ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash ||
            definition.Triggers.SingleOrDefault(trigger => trigger.Id == program.TriggerId)?.Window != context.Window) return false;
        for (var index = paidIndex + 2; index <= dyingIndex; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (!(HalfHandPaidDamageObserverEdge(index) || DamageFrameRidesOn(child, parent) || DamageObserverRidesOn(child, parent) ||
                RecoveryReplacementFrameRidesOn(child, parent) || ParticipantHandDyingRide(child, parent) ||
                parent is DyingFrame entry && ParticipantHandDyingEntryRide(child, parent, entry) ||
                index >= 2 && _resolutionStack[index - 2] is DyingFrame originalEntry && ParticipantHandDyingEntryRide(child, parent, originalEntry))) return false;
        }
        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(program.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.UseVirtualDyingAlcohol, Target: SkillProgramEffectTarget.Owner } ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame
                { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: not null,
                    LegacyDyingAlcoholReturn: { } returned, Step: ResolutionFrameStep.ResolvingEffect } use ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            use.DyingResponse is not null || use.VirtualBasicReturn is not null ||
            returned.ProgramFrameId != program.Id || returned.InstructionIndex != program.InstructionIndex ||
            returned.ProducerSource != new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId) ||
            !IsExactLegacyActualUseCompletion(use) ||
            _resolutionStack[dyingIndex + 3] is not HpChangedTriggerWindowFrame hp || hp.Id != hp.Change.Id ||
            hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id || hp.Continuation != PostEventContinuation.CardUse ||
            hp.CardId != 0 || hp.CardKind != CardKind.Alcohol || hp.Change.Kind != HpChangeKind.Recovery ||
            hp.Change.SourceSeat != dying.VictimSeat || hp.Change.TargetSeat != dying.VictimSeat ||
            hp.Change.Amount != 1 || hp.Change.HpBefore > 0 || hp.Change.HpAfter != hp.Change.HpBefore + 1 ||
            hp.Candidates.Count == 0 || hp.Candidates.Count != hp.Contexts.Count ||
            hp.CandidateIndex < 0 || hp.CandidateIndex > hp.Candidates.Count) return false;

        var history = CompleteProgramEventHistory().ToArray();
        // A paid rescue keeps its accepted source identity even if a child later
        // disables that skill. Re-enumerating live eligibility would revoke it.
        if (history.OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == program.Id &&
                fact.SkillId == program.SkillId && fact.BindingId == program.TriggerId && fact.SkillInstanceId == program.SkillInstanceId &&
                fact.OwnerSeat == program.OwnerSeat && fact.Window == context.Window) != 1 ||
            history.OfType<PlayerDyingEvent>().Count(fact => fact.ResolutionId == dying.Id &&
                fact.VictimSeat == dying.VictimSeat && fact.KillerSeat is null) != 1 ||
            history.OfType<CardUseDeclaredEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardId == 0 &&
                fact.CardKind == CardKind.Alcohol && fact.SourceSeat == dying.VictimSeat) != 1 ||
            history.OfType<TargetsConfirmedEvent>().Count(fact => fact.ResolutionId == use.Id &&
                fact.TargetSeats.SequenceEqual([dying.VictimSeat])) != 1 ||
            history.OfType<ProgramDyingRescueEvent>().Count(fact => fact.DyingFrameId == dying.Id && fact.SkillId == program.SkillId &&
                fact.OwnerSeat == program.OwnerSeat && fact.VictimSeat == dying.VictimSeat && fact.CardId == 0 &&
                fact.RecoveredHp == 1 && fact.VictimHp == hp.Change.HpAfter) != 1) return false;

        for (var index = 0; index < hp.Candidates.Count; index++)
        {
            var candidate = hp.Candidates[index]; var hpContext = hp.Contexts[index];
            if (candidate.OwnerSeat != dying.VictimSeat || hpContext.OwnerSeat != candidate.OwnerSeat ||
                hpContext.ParentFrameId != hp.Id || hpContext.HpChange != hp.Change ||
                hpContext.SourceSeat != hp.Change.SourceSeat || hpContext.TargetSeat != hp.Change.TargetSeat ||
                hpContext.Amount != hp.Change.Amount || hpContext.OccurrenceIndex != candidate.OccurrenceIndex ||
                hpContext.Window is not (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged) ||
                GetProgramTrigger(candidate).Window != hpContext.Window) return false;
        }
        if (dyingIndex + 4 == _resolutionStack.Count) return true;
        return dyingIndex + 5 == _resolutionStack.Count && hp.CandidateIndex < hp.Candidates.Count &&
            _resolutionStack[dyingIndex + 4] is ProgramSkillFrame observer &&
            MountObserverCandidateMatches(observer, hp.Candidates[hp.CandidateIndex]) &&
            observer.WindowContext == hp.Contexts[hp.CandidateIndex];
    }
}
