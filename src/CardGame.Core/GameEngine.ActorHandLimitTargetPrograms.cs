using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<int>? _actorHandLimitAnnouncedTargets;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? ActorHandLimitAnnouncedTargets
    {
        get => _actorHandLimitAnnouncedTargets;
        init => _actorHandLimitAnnouncedTargets = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed partial class GameEngine
{
    private bool HasActorHandLimitTargetPrograms =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantActorHandLimitPenalty);

    // The old target windows keep their collection rules. The new consumer sees
    // every primary target actually announced by this finalized use, including
    // future native Slash targets. It does not wait for their individual effects.
    private bool ShouldCollectActorHandLimitPenalty(CardActionContext action, int ownerSeat,
        SkillProgramTriggerWindow window)
    {
        if (!HasActorHandLimitTargetPrograms || window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            action.Type != CardActionType.Use || !IsValidPlayerSeat(ownerSeat) ||
            !_players[ownerSeat].IsAlive || ownerSeat == action.ActorSeat ||
            LifecycleActorHandLimitUse(action) is not { } use ||
            use.ActorHandLimitAnnouncedTargets?.Contains(ownerSeat) == true) return false;

        if (IsSlashCard(action.EffectiveKind))
            return action.EffectiveDesignatedTargetSeats.Contains(ownerSeat) &&
                use.TargetSeats.Contains(ownerSeat);

        if (!IsOrdinaryTrick(action.EffectiveKind)) return false;
        return action.EffectiveKind == CardKind.BorrowedSword
            ? action.TargetSeats.Where((_, index) => index % 2 == 0).Contains(ownerSeat)
            : action.EffectiveDesignatedTargetSeats.Contains(ownerSeat);
    }

    private CardUseFrame? LifecycleActorHandLimitUse(CardActionContext action) =>
        _resolutionStack.OfType<CardUseFrame>().LastOrDefault(use =>
            use.Action is { Type: CardActionType.Use } current && current.ActionId == action.ActionId &&
            current.ActorSeat == action.ActorSeat && current.ProviderSeat == action.ProviderSeat &&
            current.EffectiveKind == action.EffectiveKind && use.SourceSeat == action.ActorSeat &&
            use.CardKind == action.EffectiveKind);

    private IReadOnlyList<int> ActorHandLimitPrimaryTargets(CardActionContext action)
    {
        var targets = (action.EffectiveKind == CardKind.BorrowedSword
            ? action.TargetSeats.Where((_, index) => index % 2 == 0)
            : action.EffectiveDesignatedTargetSeats).Distinct().ToArray();
        if (targets.Any(seat => !IsValidPlayerSeat(seat)))
            throw new InvalidOperationException("An actor hand-limit announcement requires real primary target seats.");
        return Array.AsReadOnly(targets);
    }

    // Call after collecting the initial frozen candidates, even when there were
    // none. An empty opportunity is still an actual declaration: gaining a skill
    // later in this same window cannot retroactively create its old target offer.
    private void RecordActorHandLimitTargetAnnouncements(CardActionContext action,
        SkillProgramTriggerWindow window)
    {
        if (!HasActorHandLimitTargetPrograms || window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind) && !IsOrdinaryTrick(action.EffectiveKind) ||
            LifecycleActorHandLimitUse(action) is not { } use) return;
        var announced = (use.ActorHandLimitAnnouncedTargets ?? [])
            .Concat(ActorHandLimitPrimaryTargets(action)).Distinct().ToArray();
        if (use.ActorHandLimitAnnouncedTargets is { } old && old.SequenceEqual(announced)) return;
        ReplaceRuntimeFrame(use.Id, use with { ActorHandLimitAnnouncedTargets = announced });
    }

    private bool IsActorHandLimitTargetCandidate(ProgramCardTriggerCandidate candidate) =>
        _contentRegistry.Skills.GetValueOrDefault(candidate.SkillId)?.Program is { } program &&
        program.GameplayHash == candidate.GameplayHash &&
        ProgramInstructionResolver.Default.FindTrigger(program, candidate.TriggerId)?.Effects.Any(effect =>
            effect.Op == SkillProgramEffectOp.GrantActorHandLimitPenalty) == true;

    private static CardActionContext ActorHandLimitTargetCollectionAction(CardUseFrame use)
    {
        var action = use.Action!;
        if (action.TargetSeats.SequenceEqual(use.TargetSeats)) return action;
        // A paid Slash tail can redirect after its one original finalize. This
        // projection belongs only to the new collector, never to an old window.
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat,
            action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            action.EffectiveKind, use.TargetSeats, action.PhysicalCards, action.ConversionChain,
            action.EffectiveKind == CardKind.BorrowedSword
                ? use.TargetSeats.Where((_, index) => index % 2 == 0).ToArray() : use.TargetSeats,
            action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);
    }

    private bool HasIssuedActorHandLimitTargetCandidate(CardUseFrame use,
        ProgramCardTriggerCandidate candidate)
    {
        return CompleteProgramEventHistory().OfType<ProgramActorHandLimitPenaltyGrantedEvent>().Any(fact =>
            fact.Penalty.CardUseFrameId == use.Id && fact.Penalty.ActionId == use.Action!.ActionId &&
            fact.Penalty.TargetSeat == candidate.OwnerSeat &&
            fact.Penalty.Source.OwnerSeat == candidate.OwnerSeat &&
            fact.Penalty.Source.SkillId == candidate.SkillId &&
            fact.Penalty.Source.BindingId == candidate.TriggerId);
    }

    private IReadOnlyList<ProgramCardTriggerCandidate> CollectActorHandLimitTargetCandidates(
        CardUseFrame use, long windowId, IReadOnlyList<ProgramCardTriggerCandidate> existing)
    {
        var action = ActorHandLimitTargetCollectionAction(use);
        var candidates = CollectSharedCardActionCandidates(action,
                SkillProgramTriggerWindow.CardUseTargetsFinalized, use.TargetSeats,
                cardUseCausedDamage: null, continuation: IsSlashCard(use.CardKind)
                    ? ProgramCardContinuation.Slash : ProgramCardContinuation.FinalizedTrick)
            .Where(IsActorHandLimitTargetCandidate)
            .Where(candidate => ShouldCollectActorHandLimitPenalty(action, candidate.OwnerSeat,
                SkillProgramTriggerWindow.CardUseTargetsFinalized))
            .Where(candidate => !existing.Any(old => old.OwnerSeat == candidate.OwnerSeat &&
                old.SkillId == candidate.SkillId &&
                old.TriggerId == candidate.TriggerId && old.OpponentSeat == candidate.OpponentSeat))
            .Where(candidate => !HasIssuedActorHandLimitTargetCandidate(use, candidate))
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.OwnerSeat)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.TriggerId, StringComparer.Ordinal)
            .Select(candidate => candidate.FrozenContext is { CardUse: { } identity } frozen
                ? candidate with
                {
                    FrozenContext = frozen with
                    {
                        ParentFrameId = windowId,
                        CardUse = identity with { ParentCardUseFrameId = use.Id }
                    }
                }
                : throw new InvalidOperationException("An actor hand-limit target lost its frozen use identity."))
            .ToArray();
        return Array.AsReadOnly(candidates);
    }

    private ProgramCardTriggerWindowFrame AppendActorHandLimitTargetCandidates(
        ProgramCardTriggerWindowFrame frame)
    {
        if (!HasActorHandLimitTargetPrograms || _winner != Winner.None ||
            GetCardActionWindow(frame) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(frame.ParentFrameId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActionId != frame.Action.ActionId || action.ActorSeat != frame.Action.ActorSeat ||
            action.EffectiveKind != frame.Action.EffectiveKind ||
            !IsSlashCard(use.CardKind) && !IsOrdinaryTrick(use.CardKind)) return frame;

        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame current ||
            current.Id != frame.Id)
            throw new InvalidOperationException("Actor hand-limit target collection lost its exact native window.");

        var added = CollectActorHandLimitTargetCandidates(use, frame.Id, frame.Candidates);
        RecordActorHandLimitTargetAnnouncements(ActorHandLimitTargetCollectionAction(use),
            SkillProgramTriggerWindow.CardUseTargetsFinalized);
        if (added.Count == 0) return frame;
        // Keep the completed prefix, live cursor, old Action and every old frozen
        // context. An announced target still owns that frozen notification after
        // removal or effect cancellation. Some older producers do not sync Action.
        ReplaceRuntimeTop(frame = frame with
        {
            Candidates = Array.AsReadOnly(frame.Candidates.Concat(added).ToArray())
        });
        return frame;
    }

    private bool TryBeginAcceptedSlashActorHandLimitTargetPrograms(CardAttackHandle attack)
    {
        if (!HasActorHandLimitTargetPrograms || _winner != Winner.None ||
            LifecycleCardUse(attack.ResolutionId) is not
                { ProgramUseAccepted: true, Action: { Type: CardActionType.Use } action } use ||
            !IsSlashCard(use.CardKind) || action.EffectiveKind != use.CardKind ||
            action.ActorSeat != use.SourceSeat || use.CardAttack is not { } currentAttack ||
            currentAttack.TargetSeat != attack.TargetSeat || !use.TargetSeats.Contains(attack.TargetSeat)) return false;

        if (_resolutionStack.LastOrDefault()?.Id != use.Id)
            throw new InvalidOperationException("An accepted Slash target notification lost its owning use.");

        // The first finalized window announced all its then-current targets.
        // A subsequent native tail redirection announces a new target identity;
        // prior identities remain issued, and exact action/target/source facts
        // prevent either an unchanged tail or a redirect onto an old target from
        // issuing that same notification again.
        var windowId = _resolutionSequence + 1;
        var candidates = CollectActorHandLimitTargetCandidates(use, windowId, []);
        RecordActorHandLimitTargetAnnouncements(ActorHandLimitTargetCollectionAction(use),
            SkillProgramTriggerWindow.CardUseTargetsFinalized);
        if (candidates.Count == 0) return false;
        _resolutionSequence++;
        PushRuntimeFrame(new ProgramCardTriggerWindowFrame(windowId, use.Id, action,
            ProgramCardContinuation.Slash, candidates, AttackOwnerFrameId: use.Id));
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
        return true;
    }
}
