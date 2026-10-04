namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasUniqueHpPeerPrograms => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer);
    private bool IsUniqueHpPeerCandidate(ProgramTriggerCandidate c) =>
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer }];

    // A future target of the same legacy virtual Slash has no separate physical
    // action. Prove its original producer and every genuinely appended target,
    // rather than pretending it is the attack's current target.
    private ActualUseTargetIdentity? FreezeUniqueHpPeerTarget(CardUseFrame use, int target)
    {
        if (!HasUniqueHpPeerPrograms || !IsSlashCard(use.CardKind) || target == use.SourceSeat ||
            !IsValidPlayerSeat(target) || !_players[target].IsAlive || !use.TargetSeats.Contains(target)) return null;
        if (use.Action is { Type: CardActionType.Use } action && action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind)
            return new(use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, use.CardKind, target, _turnNumber, _currentSeat, null);
        if (UniqueHpLegacyTargetProducer(use, target) is not { } producer) return null;
        return new(use.Id, null, use.SourceSeat, use.SourceSeat, use.CardKind, target, _turnNumber, _currentSeat, producer);
    }

    private long? UniqueHpLegacyTargetProducer(CardUseFrame use, int target)
    {
        if (use.Action is not null || !use.TargetSeats.Contains(target) || use.CardAttack?.ProgramSkillCardUseFrameId is not { } producer ||
            !MatchesSameTypeAidLegacyProducer(use, producer) ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == producer)?.SelectedTargetSeats is not [var original] ||
            target != original && target != use.CardAttack.TargetSeat && !CompleteProgramEventHistory().OfType<SameTypeAidTargetAddedEvent>().Any(e =>
                e.RecipientSeat == target && IsSameTypeAidTargetFact(use, e))) return null;
        return producer;
    }

    private void AppendUnannouncedUniqueHpTargets(CardUseFrame supplied, long windowId,
        List<(ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)> entries)
    {
        if (!HasUniqueHpPeerPrograms || _winner != Winner.None || !IsSlashCard(supplied.CardKind)) return;
        var use = LifecycleCardUse(supplied.Id) ?? throw new InvalidOperationException("The target announcement lost its real Slash.");
        var seen = (use.UniqueHpAnnouncedTargets ?? []).ToList();
        foreach (var target in use.TargetSeats.Distinct())
        {
            if (seen.Contains(target) || FreezeUniqueHpPeerTarget(use, target) is not { } identity) continue;
            seen.Add(target);
            ReplaceRuntimeFrame(use.Id, use = use with { UniqueHpAnnouncedTargets = seen.ToArray() });
            AdvanceEventRulesAndQueueFact(new UniqueHpTargetAnnouncedEvent(windowId, identity));
            if (IsCardEffectIneffective(use.Id, target)) continue;
            var facts = use.Action is { } action ? CaptureProgramTriggerFacts(_players[target], action) : CaptureProgramTriggerFacts(_players[target]);
            foreach (var candidate in CollectEligibleProgramTriggerCandidates(_players[target], SkillProgramTriggerWindow.OtherActualUseTargeted, facts))
                if (IsUniqueHpPeerCandidate(candidate))
                    entries.Add((candidate, new(SkillProgramTriggerWindow.OtherActualUseTargeted, windowId, target,
                        SourceSeat: identity.ActorSeat, TargetSeat: target, OccurrenceIndex: candidate.OccurrenceIndex, Facts: facts)
                        { ActualUseTarget = identity }));
        }
    }

    private ActualUseTargetWindowFrame AppendUniqueHpTargetsToCurrentWindow(ActualUseTargetWindowFrame parent)
    {
        if (parent.ReturnKind == ActualUseTargetReturnKind.OrdinaryTrick || LifecycleCardUse(parent.ParentFrameId) is not { } use) return parent;
        var entries = new List<(ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)>();
        AppendUnannouncedUniqueHpTargets(use, parent.Id, entries);
        if (entries.Count == 0) return parent;
        entries = entries.OrderByDescending(e => e.Candidate.Priority).ThenBy(e => e.Candidate.OwnerSeat)
            .ThenBy(e => e.Candidate.SkillId, StringComparer.Ordinal).ToList();
        // Preserve the completed prefix, current cursor and all old candidates.
        ReplaceRuntimeTop(parent = parent with { Candidates = parent.Candidates.Concat(entries.Select(e => e.Candidate)).ToArray(),
            Contexts = parent.Contexts.Concat(entries.Select(e => e.Context)).ToArray() });
        return parent;
    }

    // Used by the already-completed 7000 batch return. This creates only 7200
    // offers for new targets; no old paid-own-target or half-hand offer is replayed.
    private bool TryBeginUnannouncedUniqueHpTargets(CardAttackHandle attack, bool legacy)
    {
        if (!HasUniqueHpPeerPrograms || _winner != Winner.None || LifecycleCardUse(attack.ResolutionId) is not { } use) return false;
        return StartActualUseTargetWindow(use, [], legacy ? ActualUseTargetReturnKind.LegacyVirtualSlash : ActualUseTargetReturnKind.Slash, null);
    }

    private bool MatchesAnnouncedUniqueHpLegacyTarget(ActualUseTargetIdentity identity)
    {
        if (!HasUniqueHpPeerPrograms || identity.ActionId is not null || LifecycleCardUse(identity.CardUseFrameId) is not { } use ||
            use.UniqueHpAnnouncedTargets?.Contains(identity.TargetSeat) != true || identity.LegacyProducerProgramId != UniqueHpLegacyTargetProducer(use, identity.TargetSeat) ||
            _resolutionStack.OfType<ActualUseTargetWindowFrame>().LastOrDefault() is not { } parent || parent.ParentFrameId != use.Id ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Contexts.Count != parent.Candidates.Count ||
            !IsUniqueHpPeerCandidate(parent.Candidates[parent.CandidateIndex]) || parent.Contexts[parent.CandidateIndex].ActualUseTarget != identity)
            return false;
        return CompleteProgramEventHistory().OfType<UniqueHpTargetAnnouncedEvent>().Count(e => e.WindowFrameId == parent.Id && e.Target == identity) == 1;
    }

    private void AssertUniqueHpTargetAnnouncements()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.UniqueHpAnnouncedTargets is not null))
        {
            var seen = use.UniqueHpAnnouncedTargets!;
            var issued = CompleteProgramEventHistory().OfType<UniqueHpTargetAnnouncedEvent>().Where(e => e.Target.CardUseFrameId == use.Id).ToArray();
            if (!HasUniqueHpPeerPrograms || !IsSlashCard(use.CardKind) || seen.Distinct().Count() != seen.Count ||
                seen.Any(s => !IsValidPlayerSeat(s)) || issued.Length != seen.Count || issued.Select(e => e.Target.TargetSeat).Distinct().Count() != issued.Length ||
                !issued.Select(e => e.Target.TargetSeat).SequenceEqual(seen) || issued.Any(e => e.Target.ActualTurnNumber != _turnNumber ||
                    e.Target.ActualTurnOwnerSeat != _currentSeat || e.WindowFrameId <= use.Id || !IsSlashCard(e.Target.EffectiveKind)))
                throw new InvalidOperationException("The once-announced target set lost its exact real Slash/window facts.");
        }
    }
}
