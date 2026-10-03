namespace CardGame.Core;

public sealed partial class GameEngine
{
    partial void TryFreezeProvenanceCounterspellSource(CharacterState owner, IReadOnlyList<int> physicalCardIds,
        ref CardConversionSource? source);

    private CardConversionSource? FreezeUnrespondableCounterspellSource(CharacterState owner, IReadOnlyList<int> physicalCardIds)
    {
        var binding = CardPolicies(owner, SkillProgramCardPolicyKind.UnrespondableNullification,
            CardKind.Nullification).FirstOrDefault();
        if (binding.Source is not null)
            return new(binding.Source.SkillId, binding.Policy.Id, owner.Seat, binding.Source.SkillInstanceId);
        CardConversionSource? source = null;
        TryFreezeProvenanceCounterspellSource(owner, physicalCardIds, ref source);
        return source;
    }

    private void IssueUnrespondableCounterspell(NullificationWindowFrame window,
        CardActionContext action, CardConversionSource? frozenSource)
    {
        if (frozenSource is null) return;
        var current = _resolutionStack.OfType<NullificationWindowFrame>().Single(frame => frame.Id == window.Id);
        var parent = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == window.ParentFrameId);
        if (action.Type != CardActionType.Response || action.EffectiveKind != CardKind.Nullification ||
            action.ActorSeat != action.ProviderSeat || action.ResponderSeat != action.ActorSeat ||
            action.RequesterSeat is not null || action.ParentActionId != parent.Action?.ActionId ||
            frozenSource.OwnerSeat != action.ActorSeat || string.IsNullOrWhiteSpace(frozenSource.SkillId) ||
            string.IsNullOrWhiteSpace(frozenSource.BindingId) || string.IsNullOrWhiteSpace(frozenSource.SkillInstanceId) ||
            action.PhysicalCards.Count == 0 || current.ChainDepth <= 0 ||
            current.UnrespondableCounterspell is not null)
            throw new InvalidOperationException("Unrespondable counterspell lost its exact paid response action.");
        var receipt = new UnrespondableCounterspellReceipt(frozenSource, current.Id,
            parent.Id, action.ActionId, action.ActorSeat, current.ChainDepth);
        ReplaceRuntimeFrame(current.Id, current with { UnrespondableCounterspell = receipt });
        AdvanceEventRulesAndQueueFact(new UnrespondableCounterspellIssuedEvent(current.Id,
            action.ActionId, action.ActorSeat, current.ChainDepth, frozenSource));
    }

    private bool TryFinishUnrespondableCounterspell(NullificationWindowFrame window)
    {
        if (window.UnrespondableCounterspell is not { } receipt) return false;
        if (window.CounterspellPayment is not null)
            throw new InvalidOperationException("Counterspell cannot finish before its exact payment children.");
        if (_resolutionStack.LastOrDefault()?.Id != window.Id || receipt.WindowFrameId != window.Id ||
            receipt.ParentCardUseFrameId != window.ParentFrameId || receipt.ChainDepth != window.ChainDepth ||
            receipt.ResponderSeat != receipt.Source.OwnerSeat ||
            !CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(fact =>
                fact.Action.ActionId == receipt.ActionId && fact.Action.EffectiveKind == CardKind.Nullification &&
                fact.Action.ActorSeat == receipt.ResponderSeat && fact.Action.ProviderSeat == receipt.ResponderSeat &&
                fact.Action.ResponderSeat == receipt.ResponderSeat && fact.Action.RequesterSeat is null &&
                fact.Action.ParentActionId == _resolutionStack.OfType<CardUseFrame>()
                    .Single(frame => frame.Id == window.ParentFrameId).Action?.ActionId))
            throw new InvalidOperationException("Unrespondable counterspell changed its issued chain node.");
        // Every accepted/committed/completed response child has already returned
        // here. This ends this counterspell node, not the original trick's policy.
        if (_winner != Winner.None)
        {
            ReplaceRuntimeTop(window with { Step = ResolutionFrameStep.Completed });
            PopResolutionFrame(window.Id, ResolutionFrameKind.NullificationWindow);
            AdvanceEventRulesAndQueueFact(new NullificationResolvedEvent(window.ParentFrameId,
                window.EffectCardId, window.EffectCardKind, window.EffectNullified, window.ChainDepth));
            ClearPendingDecision();
            foreach (var physical in GetCardUsePhysicalCards(window.ParentFrameId))
                MoveFinishedTrickCard(window.ParentFrameId, physical);
            FinishCardUse(window.ParentFrameId, GetNullificationEffectCard(window), window.EffectCardKind);
        }
        else FinishNullificationWindow(window);
        return true;
    }

    private bool TryBeginPolicyCounterspellPayment(NullificationWindowFrame window,
        CardActionContext action, CardConversionSource? frozenSource, IReadOnlyList<int> frozenMaterialIds)
    {
        if (frozenSource is null) return false;
        if (!action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(frozenMaterialIds))
            throw new InvalidOperationException("Counterspell changed its qualified material set after policy capture.");
        IssueUnrespondableCounterspell(window, action, frozenSource);
        var current = _resolutionStack.OfType<NullificationWindowFrame>().Single(frame => frame.Id == window.Id);
        ReplaceRuntimeTop(current with { CounterspellPayment = new(action) });
        ContinuePolicyCounterspellPayment(window.Id);
        return true;
    }

    private void ContinuePolicyCounterspellPayment(long windowId)
    {
        if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame frame || frame.Id != windowId ||
            frame.CounterspellPayment is not { Action: { } action } ||
            frame.UnrespondableCounterspell?.ActionId != action.ActionId)
            throw new InvalidOperationException("Counterspell payment lost its exact accepted response owner.");
        if (TryBeginHpChangedProgramWindow(windowId, PostEventContinuation.CounterspellPayment) ||
            TryBeginCardsMovedProgramWindow(windowId)) return;
        ReplaceRuntimeTop(frame with { CounterspellPayment = null });
        if (TryBeginCommittedResponseUsePrograms(null, action, ProgramCardContinuation.NullificationResponse)) return;
        if (TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardResponseAccepted, [],
            ProgramCardContinuation.NullificationResponse)) return;
        ContinueNullificationAfterResponseUse(action);
    }

    // A detached dying response has no lifecycle candidate frame. Match its
    // actual binding producer, exact runtime instance and original Dying token;
    // do not re-run the already-consumed once-game eligibility check.
    private bool PolicyCounterspellDyingProgramRidesOn(ProgramSkillFrame response, DyingFrame dying)
    {
        if (dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            response.WindowContext is not { } context || context.ParentFrameId != dying.Id ||
            context.TargetSeat != dying.VictimSeat || context.OwnerSeat != response.OwnerSeat ||
            context.SourceSeat is not null || context.DamageFrameId is not null ||
            response.OwnerSeat != dying.ResponderSeat || !IsValidPlayerSeat(response.OwnerSeat) ||
            string.IsNullOrWhiteSpace(response.TriggerId) || string.IsNullOrWhiteSpace(response.SkillInstanceId) ||
            response.ActivationId != response.TriggerId ||
            context.Window is not (SkillProgramTriggerWindow.SelfDyingResponse or SkillProgramTriggerWindow.DyingResponse) ||
            context.Window == SkillProgramTriggerWindow.SelfDyingResponse && response.OwnerSeat != dying.VictimSeat ||
            _contentRegistry.Skills.GetValueOrDefault(response.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != response.GameplayHash ||
            definition.Triggers.SingleOrDefault(binding => binding.Id == response.TriggerId) is not { } trigger ||
            trigger.Window != context.Window ||
            !CollectProgramTriggerCandidates(_players[response.OwnerSeat], context.Window, context.OccurrenceIndex)
                .Any(candidate => MountObserverCandidateMatches(response, candidate))) return false;
        return CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Any(fact =>
            fact.FrameId == response.Id && fact.SkillId == response.SkillId && fact.BindingId == response.TriggerId &&
            fact.SkillInstanceId == response.SkillInstanceId && fact.OwnerSeat == response.OwnerSeat && fact.Window == context.Window);
    }

    private bool PolicyCounterspellDyingFaceEdge(int index)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (!CharacterTurnedOverFrameRidesOn(child, parent)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame window && parent is ProgramSkillFrame response)
        {
            if (index < 2 || _resolutionStack[index - 2] is not DyingFrame dying ||
                !PolicyCounterspellDyingProgramRidesOn(response, dying) ||
                response.WindowContext?.Window != SkillProgramTriggerWindow.SelfDyingResponse ||
                window.ResumeProgramFrameId != response.Id || window.OwnerSeat != response.OwnerSeat ||
                response.InstructionIndex < 1 ||
                ProgramInstructionResolver.Default.Resolve(response, _contentRegistry.GetSkill(response.SkillId).Program!)
                    .GetPausedInstruction(response.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.TurnOver } effect ||
                ResolveProgramEffectTarget(response, effect.Target) != window.OwnerSeat) return false;
            return CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(fact =>
                fact.Change.Id == window.Id && fact.Change.ParentFrameId == response.Id &&
                fact.Change.TargetSeat == window.OwnerSeat && fact.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver &&
                fact.Change.TurnedOver is { } face && face.WasFaceDown != face.IsFaceDown);
        }
        return child is ProgramSkillFrame observer && parent is ProgramLifecycleTriggerWindowFrame owner &&
            owner.CandidateIndex >= 0 && owner.CandidateIndex < owner.Candidates.Count &&
            MountObserverCandidateMatches(observer, owner.Candidates[owner.CandidateIndex]) &&
            observer.WindowContext is { Window: SkillProgramTriggerWindow.CharacterTurnedOver } context &&
            context.ParentFrameId == owner.Id && context.OwnerSeat == observer.OwnerSeat && context.TargetSeat == owner.OwnerSeat;
    }

    // Virtual self Alcohol has no physical action or DyingResponse card token.
    // Only its actual paused producer and exact queued recovery completion may
    // retain a zero-entity Use across a committed child boundary.
    private bool PolicyCounterspellVirtualAlcoholRide(int dyingIndex, DyingFrame dying)
    {
        if (dyingIndex < 0 || dyingIndex + 3 >= _resolutionStack.Count ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame program ||
            !PolicyCounterspellDyingProgramRidesOn(program, dying) ||
            program.WindowContext?.Window != SkillProgramTriggerWindow.SelfDyingResponse ||
            program.OwnerSeat != dying.VictimSeat || program.InstructionIndex < 1 ||
            ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!)
                .GetPausedInstruction(program.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.UseVirtualDyingAlcohol, Target: SkillProgramEffectTarget.Owner } ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame use || use.CardId != 0 || use.CardKind != CardKind.Alcohol ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            use.Action is not null || use.DyingResponse is not null || use.VirtualBasicReturn is not null ||
            use.PhysicalCardIds is not { Count: 0 } ||
            _resolutionStack[dyingIndex + 3] is not RecoveryReplacementFrame recovery || recovery.ParentFrameId != use.Id ||
            recovery.Return.ResumeFrameId != use.Id || recovery.Return.Continuation != PostEventContinuation.RecoveryProducer ||
            recovery.Attempt.SourceSeat != dying.VictimSeat || recovery.Attempt.TargetSeat != dying.VictimSeat || recovery.Attempt.Amount != 1 || recovery.Attempt.HpBefore > 0 ||
            recovery.Attempt.Completion is not { Producer: RecoveryAttemptProducer.DyingVirtualAlcohol, InstructionIndex: 0, CardId: 0, CardKind: CardKind.Alcohol } producer ||
            producer.ProgramFrameId != program.Id || producer.DyingFrameId != dying.Id || producer.SkillId != program.SkillId ||
            producer.SkillOwnerSeat != program.OwnerSeat || producer.MoveReason is not null ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(fact => fact.ResolutionId == use.Id && fact.CardId == 0 &&
                fact.CardKind == CardKind.Alcohol && fact.SourceSeat == dying.VictimSeat) ||
            !CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Any(fact => fact.ResolutionId == use.Id &&
                fact.TargetSeats.SequenceEqual([dying.VictimSeat]))) return false;
        for (var index = dyingIndex + 4; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (child is ProgramSkillFrame observer && parent is HpChangedTriggerWindowFrame hp)
            {
                if (hp.CandidateIndex < 0 || hp.CandidateIndex >= hp.Candidates.Count ||
                    !MountObserverCandidateMatches(observer, hp.Candidates[hp.CandidateIndex]) ||
                    observer.WindowContext is not { HpChange: { } change } context ||
                    context.ParentFrameId != hp.Id || change != hp.Change || context.Window is not
                        (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged)) return false;
                continue;
            }
            if (child is ProgramSkillFrame observerMove && parent is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.CandidateIndex < 0 || moved.CandidateIndex >= moved.Candidates.Count ||
                    !MountObserverCandidateMatches(observerMove, moved.Candidates[moved.CandidateIndex]) ||
                    observerMove.WindowContext is not { MovementBatch: { } batch } contextMove ||
                    contextMove.ParentFrameId != moved.Id || batch.Id != moved.Batch.Id || contextMove.Window is not
                        (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived)) return false;
                continue;
            }
            if (!DamageFrameRidesOn(child, parent) &&
                !RecoveryReplacementFrameRidesOn(child, parent) && !RandomEquipmentFrameRidesOn(child, parent) &&
                !PileEquipmentFrameRidesOn(child, parent)) return false;
        }
        return true;
    }

    private bool IsPaidCounterspellProgramDying() => ActiveDying is { ResumesProgramSkill: true } dying &&
        ActiveNullificationWindow is { CounterspellPayment: not null } window &&
        _resolutionStack.OfType<DyingFrame>().Any(frame => frame.Id == dying.FrameId && frame.ParentFrameId == dying.ParentFrameId) &&
        TryGetPolicyCounterspellPaymentRide(window, out _);

    private bool TryGetPolicyCounterspellPaymentRide(NullificationWindowFrame window, out int[] rescueCosts)
    {
        rescueCosts = [];
        if (window.CounterspellPayment?.Action is not { } action ||
            window.UnrespondableCounterspell is not { } issued || issued.ActionId != action.ActionId ||
            issued.WindowFrameId != window.Id || issued.ChainDepth != window.ChainDepth ||
            issued.ParentCardUseFrameId != window.ParentFrameId || issued.ResponderSeat != action.ActorSeat)
            return false;
        var root = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        if (root < 0 || root + 1 >= _resolutionStack.Count) return false;
        CardUseFrame? rescue = null;
        for (var index = root + 1; index < _resolutionStack.Count; index++)
        {
            var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (parent.Id == window.Id)
            {
                if (child is HpChangedTriggerWindowFrame hp && hp.Continuation == PostEventContinuation.CounterspellPayment &&
                    hp.ResumeFrameId == window.Id && hp.Change.ParentFrameId == window.Id ||
                    child is RecoveryReplacementFrame replacement && replacement.ParentFrameId == window.Id &&
                    replacement.Return.ResumeFrameId == window.Id && replacement.Return.Continuation == PostEventContinuation.CounterspellPayment ||
                    child is CardsMovedTriggerWindowFrame moved && moved.ResumeCounterspellPaymentFrameId == window.Id &&
                    moved.Batch.ParentFrameId == window.Id && moved.Batch.AwaitingProgramFrameId is null)
                    continue;
                return false;
            }
            if (child is DyingFrame dying && parent is ProgramSkillFrame losing &&
                dying.Continuation == DyingContinuationKind.ProgramSkill && dying.ParentFrameId == losing.Id &&
                dying.KillerSeat is null && _contentRegistry.Skills.GetValueOrDefault(losing.SkillId)?.Program is { } program &&
                program.GameplayHash == losing.GameplayHash && losing.InstructionIndex > 0 &&
                ProgramInstructionResolver.Default.Resolve(losing, program).GetPausedInstruction(losing.InstructionIndex).Effect is
                    { Op: SkillProgramEffectOp.LoseHp } effect && ResolveProgramEffectTarget(losing, effect.Target) == dying.VictimSeat &&
                CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Any(fact =>
                    fact.FrameId == losing.Id && fact.SkillId == losing.SkillId && fact.TargetSeat == dying.VictimSeat &&
                    fact.Amount > 0 && fact.RemainingHp <= 0))
                continue;
            if (child is ProgramSkillFrame responseProgram && parent is DyingFrame responseDying &&
                PolicyCounterspellDyingProgramRidesOn(responseProgram, responseDying))
            {
                // The existing bound-pile helper proves the complete suffix,
                // including provider/material conversion and its actual ledger.
                if (IsPaidHandRepaymentProgramAlcoholRide(index - 1, responseDying))
                {
                    var alcohol = (CardUseFrame)_resolutionStack[index + 1];
                    rescueCosts = rescueCosts.Concat(alcohol.Action!.PhysicalCards
                        .Where(cost => _cardZones.GetLocation(cost.CardId) == CardLocation.Processing)
                        .Select(cost => cost.CardId)).Distinct().ToArray();
                    break;
                }
                if (PolicyCounterspellVirtualAlcoholRide(index - 1, responseDying)) break;
                continue;
            }
            if (PolicyCounterspellDyingFaceEdge(index)) continue;
            if (child is ProgramLifecycleTriggerWindowFrame entry && parent is DyingFrame entryDying &&
                entry.Window == SkillProgramTriggerWindow.DyingEntering && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
                entry.ResumeDyingFrameId == entryDying.Id && entry.OwnerSeat == entryDying.VictimSeat) continue;
            if (child is CardUseFrame use && parent is DyingFrame rescueDying && use.DyingResponse is { } response &&
                response.ResolutionId == rescueDying.Id && response.ResponderSeat == rescueDying.ResponderSeat &&
                use.SourceSeat == response.ResponderSeat && use.TargetSeats.SequenceEqual([rescueDying.VictimSeat]) &&
                use.CardKind is CardKind.Peach or CardKind.Alcohol &&
                use.Action is { Type: CardActionType.Use } rescueAction && rescueAction.ActorSeat == use.SourceSeat &&
                rescueAction.ProviderSeat == use.SourceSeat && rescueAction.RequesterSeat is null &&
                rescueAction.EffectiveKind == use.CardKind &&
                rescueAction.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(use.PhysicalCardIds ?? [use.CardId]) &&
                (use.CardKind == CardKind.Peach ? response.UsedPeach : response.UsedAlcohol && use.SourceSeat == rescueDying.VictimSeat))
            {
                rescue = use;
                rescueCosts = rescueAction.PhysicalCards.Where(cost => _cardZones.GetLocation(cost.CardId) == CardLocation.Processing)
                    .Select(cost => cost.CardId).ToArray();
                continue;
            }
            if (child is ProgramSkillFrame hpObserver && parent is HpChangedTriggerWindowFrame hpWindow)
            {
                if (hpWindow.CandidateIndex < 0 || hpWindow.CandidateIndex >= hpWindow.Candidates.Count ||
                    !MountObserverCandidateMatches(hpObserver, hpWindow.Candidates[hpWindow.CandidateIndex]) ||
                    hpObserver.WindowContext is not { HpChange: { } change } hpContext ||
                    hpContext.ParentFrameId != hpWindow.Id || change != hpWindow.Change) return false;
                continue;
            }
            if (child is ProgramSkillFrame movementObserver && parent is CardsMovedTriggerWindowFrame movementWindow)
            {
                if (movementWindow.CandidateIndex < 0 || movementWindow.CandidateIndex >= movementWindow.Candidates.Count ||
                    !MountObserverCandidateMatches(movementObserver, movementWindow.Candidates[movementWindow.CandidateIndex]) ||
                    movementObserver.WindowContext is not { MovementBatch: { } batch } movementContext ||
                    movementContext.ParentFrameId != movementWindow.Id || batch.Id != movementWindow.Batch.Id ||
                    movementContext.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or
                        SkillProgramTriggerWindow.DiscardPileReceived)) return false;
                continue;
            }
            if (rescue is not null && ParticipantHandRescueObserverRide(child, parent, rescue)) continue;
            if (child is ProgramSkillFrame candidateProgram && parent is ProgramLifecycleTriggerWindowFrame candidateEntry &&
                candidateProgram.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering } candidateContext &&
                candidateContext.ParentFrameId == candidateEntry.Id &&
                candidateEntry.Window == SkillProgramTriggerWindow.DyingEntering &&
                candidateEntry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
                candidateEntry.CandidateIndex >= 0 &&
                candidateEntry.CandidateIndex < candidateEntry.Candidates.Count)
            {
                var candidate = candidateEntry.Candidates[candidateEntry.CandidateIndex];
                if (index >= 2 && _resolutionStack[index - 2] is DyingFrame entrySource &&
                    candidateEntry.ResumeDyingFrameId == entrySource.Id && candidateEntry.OwnerSeat == entrySource.VictimSeat &&
                    candidateContext.OwnerSeat == candidateProgram.OwnerSeat && candidateContext.TargetSeat == entrySource.VictimSeat &&
                    candidateContext.SourceSeat == entrySource.KillerSeat && MountObserverCandidateMatches(candidateProgram, candidate))
                    continue;
            }
            if (DamageFrameRidesOn(child, parent) || DamageObserverRidesOn(child, parent) ||
                RecoveryReplacementFrameRidesOn(child, parent) || RandomEquipmentFrameRidesOn(child, parent) ||
                PileEquipmentFrameRidesOn(child, parent)) continue;
            return false;
        }
        // Only after proving the exact paid node and every contiguous typed
        // edge may its actual random-equipment child contribute Processing.
        // This is not a general CardUse presence exemption.
        var childCosts = rescueCosts.ToHashSet();
        for (var index = root + 1; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not CardUseFrame equipment ||
                _resolutionStack[index - 1] is not ProgramSkillFrame producer ||
                !RandomEquipmentFrameRidesOn(equipment, producer) ||
                equipment.Action is not { Type: CardActionType.Use, PhysicalCards: [var material] } equipmentAction ||
                equipmentAction.ActorSeat != equipment.SourceSeat || equipmentAction.ProviderSeat != equipment.SourceSeat ||
                equipmentAction.RequesterSeat is not null || equipmentAction.EffectiveKind != equipment.CardKind ||
                material.CardId != equipment.CardId || material.From != CardLocation.DrawPile ||
                !(equipment.PhysicalCardIds ?? [equipment.CardId]).SequenceEqual([material.CardId]) ||
                _cardZones.GetLocation(material.CardId) != CardLocation.Processing) continue;
            childCosts.Add(material.CardId);
        }
        rescueCosts = childCosts.Order().ToArray();
        return true;
    }
}
