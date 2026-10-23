namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 46700)]
    private void CollectMaTieIssuedReward(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCommitted || action.Type != CardActionType.Use || _winner != Winner.None ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { AnniversaryMaTieOrigin: { } n, ProgramUseCommitted: true } use ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is not { } parent || !_players[parent.OwnerSeat].IsAlive || !MaTieNativeMatches(use, parent) ||
            ProgramEventHistory<AnniversaryMaTieRewardIssuedEvent>().Any(e => e.UseId == n.UseId)) return;
        if (!result.Any(c => c.OwnerSeat == parent.OwnerSeat && c.SkillId == AnniversaryMaTieComposition.Quxian && c.TriggerId == "issued-slash-reward" && c.SkillInstanceId == n.Source.SkillInstanceId))
        {
            var facts = CaptureProgramTriggerFacts(_players[parent.OwnerSeat], action) with { CardUseCausedDamage = use.CausedDamage };
            var context = CreateCardActionProgramContext(action, window, 0, parent.OwnerSeat, action.ActorSeat, facts);
            result.Add(new(parent.OwnerSeat, action.ActorSeat, AnniversaryMaTieComposition.Quxian, "issued-slash-reward", n.Hash, n.Source.SkillInstanceId, FrozenContext: context));
        }
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 46700)]
    private bool IsIssuedMaTieReward(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => c.SkillId == AnniversaryMaTieComposition.Quxian && c.BindingId == "issued-slash-reward" &&
        MaTieRewardUse(c.OwnerSeat, w) is { AnniversaryMaTieOrigin: { } n } && c.SkillInstanceId == n.Source.SkillInstanceId && c.GameplayHash == n.Hash;
    private CardUseFrame? MaTieCardWindow(ProgramSkillWindowContext c, SkillProgramTriggerWindow window)
    {
        if (c.Window != window || c.CardUse is not { } card || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != card.ParentCardUseFrameId || parent.Action.ActionId != card.CardActionId || LifecycleCardUse(parent.ParentFrameId) is not { Action: { } action } use || action.ActionId != card.CardActionId) return null;
        return use;
    }
    private CardUseFrame? MaTieRewardUse(int owner, ProgramSkillWindowContext c)
    {
        if (MaTieCardWindow(c, SkillProgramTriggerWindow.CardUseCommitted) is not { ProgramUseCommitted: true, AnniversaryMaTieOrigin: { } n } use ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == n.FrameId) is not { } f || f.OwnerSeat != owner || !MaTieNativeMatches(use, f) ||
            ProgramEventHistory<AnniversaryMaTieRewardIssuedEvent>().Any(e => e.UseId == n.UseId)) return null;
        return use;
    }
    private bool MaTieCardParent(ProgramSkillFrame f, SkillProgramTriggerWindow window)
    {
        if (f.ActivationId != f.TriggerId || f.WindowContext is not { } c || MaTieCardWindow(c, window) is null ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])) || f.WindowContext != CreateCardActionProgramContext(parent, parent.Candidates[parent.CandidateIndex])) return false;
        return true;
    }
    private SkillProgramStepOutcome BeginMaTieReward(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryMaTieComposition.Quxian || f.TriggerId != "issued-slash-reward" || f.InstructionIndex != 1 || f.AnniversaryMaTie is not null || !MaTieCardParent(f, SkillProgramTriggerWindow.CardUseCommitted) || MaTieRewardUse(f.OwnerSeat, f.WindowContext!) is not { AnniversaryMaTieOrigin: { } n } use)
            throw new InvalidOperationException("Quxian reward requires this exact once-committed issued actual Slash.");
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == n.FrameId); var original = parent.AnniversaryMaTie!;
        ReplaceRuntimeFrame(parent.Id, parent with { AnniversaryMaTie = original with { Used = original.Used.Contains(n.Actor) ? original.Used : original.Used.Append(n.Actor).ToArray() } });
        var r = new AnniversaryMaTieReceipt(MaTieSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, AnniversaryMaTieStage.RewardDrawing)
        { RewardParent = parent.Id, UseId = use.Id, ActionId = n.ActionId, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.TurnOwner));
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieRewardIssuedEvent(f.Id, parent.Id, n.Index, n.UseId, n.ActionId));
        BeginOperationProgramDraw(f, f.OwnerSeat, 1, new("skill-program.anniversary:quxian.used-slash-draw")); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward)]
    private void ReturnMaTieRewardDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!ValidMaTie(f) || f.AnniversaryMaTie is not { Stage: AnniversaryMaTieStage.RewardDrawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Quxian cannot substitute or repeat its issued one-card native draw.");
        ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.RewardChildren, Draw = outcome, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieRewardReturnedEvent(f.Id, r.RewardParent, r.UseId, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter)); ContinueMaTie(f.Id);
    }
    private SkillProgramStepOutcome RecordMaTieZhuiji(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryMaTieComposition.Zhuiji || f.TriggerId != "completed-other-targets" || f.InstructionIndex != 1 || !MaTieCardParent(f, SkillProgramTriggerWindow.CardUseCompleted) ||
            MaTieCardWindow(f.WindowContext!, SkillProgramTriggerWindow.CardUseCompleted) is not { Action: { Type: CardActionType.Use } action } use || action.ActorSeat != f.OwnerSeat)
            throw new InvalidOperationException("Zhuiji needs its exact completed actual use and final other character targets.");
        foreach (var target in action.EffectiveDesignatedTargetSeats.Where(t => t != f.OwnerSeat).Distinct())
        {
            if (!IsValidPlayerSeat(target) || ProgramEventHistory<AnniversaryMaTieDistanceEvent>().Any(e => e.ActionId == action.ActionId && e.Source.OwnerSeat == f.OwnerSeat && e.Target == target)) continue;
            AdvanceEventRulesAndQueueFact(new AnniversaryMaTieDistanceEvent(f.Id, MaTieSource(f), f.GameplayHash, use.Id, action.ActionId, _turnNumber, _turnProgression.OwnerSeat, target));
        }
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 46700)]
    private RuleQueryEvaluation ApplyMaTieZhuiji(CharacterState source, CharacterState target, RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat || !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji) ||
            EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat)) return original;
        var grants = ProgramEventHistory<AnniversaryMaTieDistanceEvent>().Where(e => e.Source.OwnerSeat == source.Seat && e.Target == target.Seat && e.Turn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat &&
            e.Source.SkillId == AnniversaryMaTieComposition.Zhuiji && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(b => b.FrameId == e.FrameId && b.OwnerSeat == e.Source.OwnerSeat && b.SkillId == e.Source.SkillId && b.SkillInstanceId == e.Source.SkillInstanceId && b.BindingId == e.Source.BindingId && b.Window == SkillProgramTriggerWindow.CardUseCompleted) == 1).ToArray();
        if (grants.Length == 0) return original;
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue), [new RuleQueryBaseTerm("distance:before-anniversary-zhuiji", ConvertRuleValue(original))],
            grants.Select(g => (RuleQueryContribution)new FiniteRuleQueryContribution($"anniversary-zhuiji:{g.FrameId}:{g.Target}", SkillRuleOperation.Set, 1)).ToArray());
    }
}
