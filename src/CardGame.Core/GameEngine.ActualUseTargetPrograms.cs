namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksPaidOwnTargets => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferHalfHandRecipientSupport) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer) || HasSameTypeActualUseAid;

    private ActualUseTargetIdentity? FreezeActualUseTarget(CardUseFrame use, int target)
    {
        if (!TracksPaidOwnTargets || !IsValidPlayerSeat(target) || target == use.SourceSeat && !HasHalfHandSelfTargetSupport(target) || !_players[target].IsAlive ||
            !use.TargetSeats.Contains(target) && !IsHalfHandImplicitSelfTarget(use, target) || !IsSlashCard(use.CardKind) && !IsOrdinaryTrick(use.CardKind)) return null;
        if (use.CardKind == CardKind.BorrowedSword && !use.TargetSeats.Where((_, i) => i % 2 == 0).Contains(target)) return null;
        if (use.Action is { Type: CardActionType.Use } action && action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind)
            return new(use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, target,
                _turnNumber, _currentSeat, null);
        return use.Action is null && LegacyDamageJudgmentVirtualProducer(use, target) is { } producer
            ? new(use.Id, null, use.SourceSeat, use.SourceSeat, use.CardKind, target, _turnNumber, _currentSeat, producer) : null;
    }

    private bool MatchesActualUseTarget(ActualUseTargetIdentity identity)
    {
        if (LifecycleCardUse(identity.CardUseFrameId) is not { } use || use.SourceSeat != identity.ActorSeat ||
            use.CardKind != identity.EffectiveKind || !use.TargetSeats.Contains(identity.TargetSeat) && !IsHalfHandImplicitSelfTarget(use, identity.TargetSeat) ||
            identity.ActualTurnNumber != _turnNumber || identity.ActualTurnOwnerSeat != _currentSeat) return false;
        if (identity.ActionId is { } actionId)
            return identity.LegacyProducerProgramId is null && use.Action is { Type: CardActionType.Use } action &&
                action.ActionId == actionId && action.ActorSeat == identity.ActorSeat && action.ProviderSeat == identity.ProviderSeat &&
                action.EffectiveKind == identity.EffectiveKind;
        return identity.LegacyProducerProgramId is { } producer && use.Action is null &&
            (LegacyDamageJudgmentVirtualProducer(use, identity.TargetSeat) == producer || MatchesAnnouncedUniqueHpLegacyTarget(identity));
    }

    private bool TryBeginActualUseTargetPrograms(CardAttackHandle attack, ActualUseTargetReturnKind kind)
    {
        var use = LifecycleCardUse(attack.ResolutionId);
        return use is not null && StartActualUseTargetWindow(use, [attack.TargetSeat], kind, null);
    }
    private bool TryBeginActualUseTargetPrograms(JizhiResolution pending)
    {
        var use = LifecycleCardUse(pending.ResolutionId);
        return use is not null && IsOrdinaryTrick(use.CardKind) && StartActualUseTargetWindow(use,
            use.CardKind == CardKind.BorrowedSword ? pending.TargetSeats.Take(1).ToArray() : HalfHandActualTrickTargets(use, pending.TargetSeats),
            ActualUseTargetReturnKind.OrdinaryTrick, new(pending.Card.Id, pending.ActionKind, pending.TargetCardId, pending.RequiredCardKind));
    }
    private bool StartActualUseTargetWindow(CardUseFrame use, IReadOnlyList<int> targets,
        ActualUseTargetReturnKind kind, ActualUseTargetTrickReturn? trick)
    {
        if (!TracksPaidOwnTargets || _winner != Winner.None) return false;
        var id = _resolutionSequence + 1;
        var entries = new List<(ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)>();
        foreach (var target in targets.Distinct())
        {
            var identity = FreezeActualUseTarget(use, target);
            if (identity is null || IsCardEffectIneffective(use.Id, target)) continue;
            var facts = use.Action is { } action
                ? CaptureProgramTriggerFacts(_players[target], action)
                : CaptureProgramTriggerFacts(_players[target]);
            foreach (var candidate in CollectEligibleProgramTriggerCandidates(_players[target], SkillProgramTriggerWindow.OtherActualUseTargeted, facts))
            {
                if (!GetProgramTrigger(candidate).Effects.Any(e => e.Op is SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget or SkillProgramEffectOp.OfferHalfHandRecipientSupport ||
                    e.Op == SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget && identity.ActionId is null)) continue;
                entries.Add((candidate, new(SkillProgramTriggerWindow.OtherActualUseTargeted, id, target,
                    SourceSeat: identity.ActorSeat, TargetSeat: target, OccurrenceIndex: candidate.OccurrenceIndex, Facts: facts)
                    { ActualUseTarget = identity }));
            }
        }
        var previouslyAnnounced = use.UniqueHpAnnouncedTargets?.Count ?? 0;
        AppendUnannouncedUniqueHpTargets(use, id, entries);
        if (entries.Count == 0 && (LifecycleCardUse(use.Id)?.UniqueHpAnnouncedTargets?.Count ?? 0) == previouslyAnnounced) return false;
        entries = entries.OrderByDescending(e => e.Candidate.Priority).ThenBy(e => e.Candidate.OwnerSeat)
            .ThenBy(e => e.Candidate.SkillId, StringComparer.Ordinal).ToList();
        _resolutionSequence = id;
        PushRuntimeFrame(new ActualUseTargetWindowFrame(id, use.Id, kind,
            Array.AsReadOnly(entries.Select(e => e.Candidate).ToArray()), Array.AsReadOnly(entries.Select(e => e.Context).ToArray()), trick));
        AdvanceRuntimeTop<ActualUseTargetWindowFrame>();
        return true;
    }

    private bool CanRunActualUseTarget(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.OtherActualUseTargeted) return true;
        return context.ActualUseTarget is { } use && use.TargetSeat == candidate.OwnerSeat && context.TargetSeat == use.TargetSeat &&
            context.SourceSeat == use.ActorSeat && (candidate.OwnerSeat != use.ActorSeat || IsHalfHandSupportCandidate(candidate)) && _players[candidate.OwnerSeat].Hp > 0 &&
            MatchesActualUseTarget(use) && !IsCardEffectIneffective(use.CardUseFrameId, use.TargetSeat) &&
            _resolutionStack.OfType<ActualUseTargetWindowFrame>().LastOrDefault() is { } parent && parent.Id == context.ParentFrameId &&
            parent.ParentFrameId == use.CardUseFrameId && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            parent.Candidates[parent.CandidateIndex] == candidate && parent.Contexts[parent.CandidateIndex].ActualUseTarget == use;
    }

    private void ContinueActualUseTargetWindowCore()
    {
        while (_resolutionStack.LastOrDefault() is ActualUseTargetWindowFrame parent)
        {
            parent = AppendUniqueHpTargetsToCurrentWindow(parent);
            if (parent.CandidateIndex >= parent.Candidates.Count || _winner != Winner.None)
            {
                PopResolutionFrame(parent.Id, ResolutionFrameKind.ActualUseTargetWindow);
                ContinueAfterActualUseTargets(parent);
                return;
            }
            var candidate = parent.Candidates[parent.CandidateIndex];
            var context = parent.Contexts[parent.CandidateIndex];
            if (!CanRunProgramTrigger(candidate, context)) { AdvanceActualUseTargetCandidate(parent, false, false); continue; }
            ReplaceRuntimeTop(parent with { Step = ResolutionFrameStep.AwaitingResponse });
            ExposeProgramTriggerDecision(candidate, context);
            return;
        }
    }
    private void AdvanceActualUseTargetCandidate(ActualUseTargetWindowFrame parent, bool activated, bool completed)
    {
        var c = parent.Candidates[parent.CandidateIndex];
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(parent.Id, c.SkillId, c.BindingId, c.SkillInstanceId,
            c.OwnerSeat, SkillProgramTriggerWindow.OtherActualUseTargeted, activated, completed));
        ReplaceRuntimeTop(parent with { CandidateIndex = parent.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
    }
    private void CompleteActualUseTargetBinding(ProgramSkillFrame child, bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not ActualUseTargetWindowFrame parent ||
            parent.Id != child.WindowContext?.ParentFrameId || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(child, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("The paid target binding lost its exact owning candidate.");
        // The binding fact has already been committed by the standard completion path.
        ReplaceRuntimeTop(parent with { CandidateIndex = parent.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceRuntimeTop<ActualUseTargetWindowFrame>();
    }
    private void ContinueAfterActualUseTargets(ActualUseTargetWindowFrame parent)
    {
        if (LifecycleCardUse(parent.ParentFrameId) is not { } use) throw new InvalidOperationException("The target window lost its actual use.");
        if (_winner != Winner.None)
        {
            if (parent.ReturnKind != ActualUseTargetReturnKind.OrdinaryTrick)
            {
                var finishedAttack = ActiveCardAttack;
                if (finishedAttack is null || finishedAttack.ResolutionId != use.Id) throw new InvalidOperationException("Winner cleanup lost its exact Slash.");
                SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect); CompleteAttack(finishedAttack); return;
            }
            // No new effect/card-response window after a winner. Clean only this use's paid physical entities.
            foreach (var id in use.PhysicalCardIds ?? [use.CardId])
                if (id != 0 && !IsCurrentUsePhysicalCardClaim(use.Id, id) && _cardZones.GetLocation(id) == CardLocation.Processing)
                    MoveCard(_cardZones.CardsAt(CardLocation.Processing).Single(c => c.Id == id), CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
            QueueCardUseFinishedWithoutPop(use.Id, new Card(use.CardId, use.CardKind, Suit.None, 0), use.CardKind);
            PopFinishedCardUse(use.Id);
            if (_resolutionStack.Count == 0 && _status != EngineStatus.Completed) CompleteGame();
            return;
        }
        if (parent.ReturnKind == ActualUseTargetReturnKind.OrdinaryTrick)
        {
            var ret = parent.TrickReturn ?? throw new InvalidOperationException("Missing ordinary-trick return.");
            var action = use.Action ?? throw new InvalidOperationException("An ordinary-trick target window requires an actual action.");
            var pending = new JizhiResolution(action.ActorSeat, use.Id, GetTrickRepresentation(use.Id, ret.EffectCardId, true),
                use.CardKind, use.CardKind == CardKind.BorrowedSword ? use.TargetSeats.Skip(use.TargetIndex).Take(2).ToArray() : use.TargetSeats,
                ret.ActionKind, ret.TargetCardId, ret.RequiredCardKind);
            if (!TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseBeforeTargetEffects, use.TargetSeats,
                ProgramCardContinuation.BeforeTrickTargetEffects, trickContinuation: new(ret.EffectCardId, ret.ActionKind, ret.TargetCardId, ret.RequiredCardKind)))
                ContinueJizhiOrNullificationAfterTargetTriggers(pending);
            return;
        }
        var attack = ActiveCardAttack;
        if (attack is null || attack.ResolutionId != use.Id) throw new InvalidOperationException("The target return lost its actual Slash.");
        if (TryBeginSlashTargetBenefits(attack, parent.ReturnKind == ActualUseTargetReturnKind.LegacyVirtualSlash, afterActualTargets: true)) return;
        if (parent.ReturnKind == ActualUseTargetReturnKind.LegacyVirtualSlash)
        {
            if (IsCardEffectIneffective(use.Id, attack.TargetSeat)) { SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect); CompleteAttack(attack); }
            else ContinueSlashAfterResponsePrograms(attack);
        }
        else if (!TryBeginProgramCardUseBeforeTargetEffects(attack)) ContinueSlashAfterProgramTargetEffects(attack);
    }
}
