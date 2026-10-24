namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasBoundaryWuYi => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmBoundaryWuYiBenxi);
    private static CardConversionSource BoundaryWuYiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class BoundaryWuYiHost(GameEngine engine) : IBoundaryWuYiHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ExecuteBoundaryWuYi(f, op); }

    private SkillProgramStepOutcome ExecuteBoundaryWuYi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (op == SkillProgramEffectOp.PrepareBoundaryWuYiBenxi)
        {
            if (f.BoundaryWuYiPhase is not null || f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
                _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
                CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat)
                throw new InvalidOperationException("Benxi requires its original actual own Play-start window.");
            ReplaceRuntimeTop(f with { BoundaryWuYiPhase = new(BoundaryWuYiSource(f), f.GameplayHash, phase, _movementSequence) });
            return SkillProgramStepOutcome.Continue;
        }
        if (op == SkillProgramEffectOp.ArmBoundaryWuYiBenxi)
        {
            if (f.BoundaryWuYiPhase is not { } r || r.Source != BoundaryWuYiSource(f) || r.Hash != f.GameplayHash || !BoundaryWuYiCostPaid(f))
                throw new InvalidOperationException("Benxi requires its original completed native whole-card discard.");
            if (CurrentActualDiscardRecoveryPhase() != r.Phase || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
                return SkillProgramStepOutcome.Continue;
            if (ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Benxi cannot issue the same paid phase twice.");
            AdvanceEventRulesAndQueueFact(new BoundaryWuYiBenxiGrantedEvent(f.Id, r.Source, r.Hash, r.Phase,
                GetProgramCardSet(f, BoundaryWuYiComposition.Cost).CardIds.Count, r.Before, _movementSequence));
            return SkillProgramStepOutcome.Continue;
        }
        if (op == SkillProgramEffectOp.ResolveBoundaryWuYiTargets) return BeginBoundaryWuYiTargets(f);
        if (!CanOfferBoundaryWuYiReward(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), f.WindowContext!))
            throw new InvalidOperationException("Benxi reward requires its exact once-completed issued card's actual damage.");
        var identity = f.WindowContext!.CardUse!;
        var grant = BoundaryWuYiIssuedGrant(identity.ParentCardUseFrameId, identity.CardActionId, f.OwnerSeat, f.SkillInstanceId)!;
        AdvanceEventRulesAndQueueFact(new BoundaryWuYiRewardIssuedEvent(f.Id, grant.FrameId, identity.ParentCardUseFrameId, identity.CardActionId, f.OwnerSeat));
        return SkillProgramStepOutcome.Continue;
    }

    private bool BoundaryWuYiCostPaid(ProgramSkillFrame f)
    {
        if (f.BoundaryWuYiPhase is not { } r || f.CardSetBindings.SingleOrDefault(b => b.Name == BoundaryWuYiComposition.Cost) is not { CardIds.Count: > 0 } cost)
            return false;
        return cost.CardIds.All(id => _cardMovements.Count(m => m.Sequence > r.Before && m.CardId == id &&
            m.From.OwnerSeat == f.OwnerSeat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program." + f.SkillId + ".MoveBoundCards") == 1);
    }

    private bool ValidBoundaryWuYiGrant(BoundaryWuYiBenxiGrantedEvent grant) => grant.Source.SkillId == BoundaryWuYiComposition.Skill &&
        grant.Source.BindingId == BoundaryWuYiComposition.Start && grant.X > 0 && grant.Before <= grant.After &&
        grant.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && grant.Phase.ActorSeat == grant.Source.OwnerSeat &&
        IsRecordedActualDiscardRecoveryPhase(grant.Phase) && _contentRegistry.GetSkill(grant.Source.SkillId).Program?.GameplayHash == grant.Hash &&
        ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().Count(e => e == grant) == 1;

    private IEnumerable<BoundaryWuYiBenxiGrantedEvent> ActiveBoundaryWuYiGrants(int owner)
    {
        if (!HasBoundaryWuYi || ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().Count == 0 ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != owner) return [];
        return ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().Where(g => g.Source.OwnerSeat == owner && g.Phase == phase && ValidBoundaryWuYiGrant(g));
    }

    [ProgramDistanceRule(ProgramDistanceStage.DistanceBaseTerms, 53100)]
    private void BoundaryWuYiDistance(ProgramDistanceQuery query, List<RuleQueryBaseTerm> terms)
    {
        if (query.Source.Seat == query.Target.Seat) return;
        foreach (var grant in ActiveBoundaryWuYiGrants(query.Source.Seat))
            terms.Add(new("issued:boundary-wu-yi:" + grant.FrameId, -grant.X));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 53100)]
    private void ConsumeBoundaryWuYiNextUse(IGameEvent payload)
    {
        if (!HasBoundaryWuYi) return;
        CardActionContext? action = null;
        long useId = 0;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId)?.Action is { } used)
        { action = used; useId = declared.ResolutionId; }
        else if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted)
            action = accepted.Action;
        // The shared ledger is entered only for genuine uses; Slash played to
        // a Duel or a supplied faction response never gains an entry here.
        if (action is null || !_actualPlayPhaseUseActions.Contains(action.ActionId) ||
            GetProgramCardCategory(action.EffectiveKind) != SkillProgramCardCategory.Basic && !IsOrdinaryTrick(action.EffectiveKind)) return;
        foreach (var grant in ActiveBoundaryWuYiGrants(action.ActorSeat).Where(g =>
            !ProgramEventHistory<BoundaryWuYiBenxiConsumedEvent>().Any(e => e.GrantFrameId == g.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new BoundaryWuYiBenxiConsumedEvent(grant.FrameId, action.ActionId, useId,
                action.ActorSeat, action.EffectiveKind, grant.Phase));
    }

    private BoundaryWuYiBenxiGrantedEvent? BoundaryWuYiIssuedGrant(long useId, long actionId, int owner, string instance) =>
        ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().SingleOrDefault(g => g.Source.OwnerSeat == owner &&
            g.Source.SkillInstanceId == instance && ValidBoundaryWuYiGrant(g) &&
            ProgramEventHistory<BoundaryWuYiBenxiConsumedEvent>().Count(e => e.GrantFrameId == g.FrameId && e.UseId == useId &&
                e.ActionId == actionId && e.Actor == owner && e.Phase == g.Phase) == 1);

    private bool CanOfferBoundaryWuYiTargets(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == BoundaryWuYiComposition.Skill && c.BindingId == BoundaryWuYiComposition.Targets && context.CardUse is { } identity &&
        BoundaryWuYiIssuedGrant(identity.ParentCardUseFrameId, identity.CardActionId, c.OwnerSeat, c.SkillInstanceId) is not null &&
        TryGetDesignatedExtraTargetUse(c.OwnerSeat, context, out var use, out var window) &&
        DesignatedExtraTargetCandidateMatches(c, context, window) &&
        !ProgramEventHistory<DesignatedExtraTargetOfferedEvent>().Any(e => e.CardUseFrameId == use.Id && e.Source.SkillId == c.SkillId && e.Source.SkillInstanceId == c.SkillInstanceId);

    private bool CanOfferBoundaryWuYiReward(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == BoundaryWuYiComposition.Skill && c.BindingId == BoundaryWuYiComposition.Reward && _players[c.OwnerSeat].IsAlive &&
        context.Window == SkillProgramTriggerWindow.CardUseCompleted && context.CardUse is { } identity &&
        BoundaryWuYiIssuedGrant(identity.ParentCardUseFrameId, identity.CardActionId, c.OwnerSeat, c.SkillInstanceId) is { } grant &&
        LifecycleCardUse(identity.ParentCardUseFrameId) is { Step: ResolutionFrameStep.Completed, Action: { } action } use &&
        action.ActionId == identity.CardActionId && action.ActorSeat == c.OwnerSeat &&
        ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1 &&
        ProgramEventHistory<DamageAppliedEvent>().Any(e => e.SourceCardUseResolutionId == use.Id && e.Amount > 0) &&
        !ProgramEventHistory<BoundaryWuYiRewardIssuedEvent>().Any(e => e.GrantFrameId == grant.FrameId);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 53100, SkillProgramEffectOp.ResolveBoundaryWuYiTargets, SkillProgramEffectOp.RewardBoundaryWuYiUse)]
    private bool QualifyBoundaryWuYi(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.BindingId == BoundaryWuYiComposition.Targets ? CanOfferBoundaryWuYiTargets(c, context) : CanOfferBoundaryWuYiReward(c, context);

    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 53100)]
    private bool BoundaryWuYiNeedsFinalizedTrickWindow(ProgramTargetCardWindowQuery query) => HasBoundaryWuYi;

    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 53100)]
    private void CollectIssuedBoundaryWuYi(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasBoundaryWuYi || window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardUseCompleted) ||
            _resolutionStack.LastOrDefault() is not CardUseFrame use || use.Action?.ActionId != action.ActionId) return;
        var binding = window == SkillProgramTriggerWindow.CardUseTargetsFinalized ? BoundaryWuYiComposition.Targets : BoundaryWuYiComposition.Reward;
        foreach (var grant in ProgramEventHistory<BoundaryWuYiBenxiGrantedEvent>().Where(g =>
            BoundaryWuYiIssuedGrant(use.Id, action.ActionId, action.ActorSeat, g.Source.SkillInstanceId) == g).ToArray())
        {
            if (result.Any(c => c.SkillId == grant.Source.SkillId && c.TriggerId == binding && c.OwnerSeat == grant.Source.OwnerSeat && c.SkillInstanceId == grant.Source.SkillInstanceId)) continue;
            var trigger = _contentRegistry.GetSkill(grant.Source.SkillId).Program!.Triggers.Single(t => t.Id == binding);
            var context = CreateCardActionProgramContext(action, window, 0, grant.Source.OwnerSeat, grant.Source.OwnerSeat,
                CaptureProgramTriggerFacts(_players[grant.Source.OwnerSeat], action));
            result.Add(new(grant.Source.OwnerSeat, grant.Source.OwnerSeat, grant.Source.SkillId, binding, grant.Hash, grant.Source.SkillInstanceId, trigger.Priority, context));
        }
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 53100)]
    private bool AdmitIssuedBoundaryWuYi(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == BoundaryWuYiComposition.Skill && (CanOfferBoundaryWuYiTargets(c, context) || CanOfferBoundaryWuYiReward(c, context));

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 53100)]
    private bool KeepBoundaryWuYiIssuedFrame(ProgramSkillFrame f) =>
        f.BoundaryWuYiPhase is not null && BoundaryWuYiCostPaid(f) || f.BoundaryWuYiTargets is not null ||
        f.SkillId == BoundaryWuYiComposition.Skill && f.TriggerId is BoundaryWuYiComposition.Targets or BoundaryWuYiComposition.Reward && f.WindowContext?.CardUse is { } identity &&
        BoundaryWuYiIssuedGrant(identity.ParentCardUseFrameId, identity.CardActionId, f.OwnerSeat, f.SkillInstanceId) is not null;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 53100)]
    private void AssertBoundaryWuYiFrame(ProgramSkillFrame f)
    {
        if (f.BoundaryWuYiPhase is { } phase && (phase.Source != BoundaryWuYiSource(f) || phase.Hash != f.GameplayHash ||
            f.TriggerId != BoundaryWuYiComposition.Start || f.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
            !IsRecordedActualDiscardRecoveryPhase(phase.Phase) || phase.Before < 0 || phase.Before > _movementSequence))
            throw new InvalidOperationException("Benxi lost its original paid Play phase and source identity.");
        if (f.BoundaryWuYiTargets is not null) AssertBoundaryWuYiTargets(f);
    }
}
