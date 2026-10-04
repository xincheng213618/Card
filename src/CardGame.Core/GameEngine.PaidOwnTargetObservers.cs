namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PaidOwnTargetMatches(ProgramSkillFrame root, ActualUseTargetWindowFrame parent, bool requireCost)
    {
        if (root.PaidOwnTarget is not { } paid || root.WindowContext is not { ActualUseTarget: { } original } context ||
            context.Window != SkillProgramTriggerWindow.OtherActualUseTargeted || context.ParentFrameId != parent.Id ||
            parent.ParentFrameId != paid.Use.CardUseFrameId || paid.Use != original || root.OwnerSeat != paid.Use.TargetSeat ||
            paid.Source != new CardConversionSource(root.SkillId, GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId) ||
            paid.GameplayHash != root.GameplayHash || paid.ActualLost != 1 || paid.HpBefore < 1 || paid.HpAfter != paid.HpBefore - 1 ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(root, parent.Candidates[parent.CandidateIndex]) || !MatchesActualUseTarget(paid.Use)) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
        if (paid.InstructionIndex != 0 || plan.Instructions.Count == 0 ||
            plan.Instructions[0].Op != SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget) return false;
        if (!requireCost) return true;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<PaidOwnTargetHpEvent>().Count(e => e.ProgramFrameId == root.Id &&
            e.Receipt == (paid with { Applied = false })) == 1 &&
            history.OfType<ProgramSkillHpLostEvent>().Count(e => e.FrameId == root.Id) == 1 &&
            history.OfType<ProgramSkillHpLostEvent>().Any(e => e.FrameId == root.Id && e.SkillId == root.SkillId &&
                e.TargetSeat == root.OwnerSeat && e.Amount == 1 && e.RemainingHp == paid.HpAfter) &&
            (!paid.Applied || history.OfType<PaidOwnTargetAppliedEvent>().Count(e => e.ProgramFrameId == root.Id &&
                e.CardUseFrameId == paid.Use.CardUseFrameId && e.ActionId == paid.Use.ActionId && e.ActorSeat == paid.Use.ActorSeat &&
                e.TargetSeat == root.OwnerSeat && e.EffectiveKind == paid.Use.EffectiveKind && e.Source == paid.Source &&
                e.GameplayHash == paid.GameplayHash) == 1);
    }

    private SkillProgramStepOutcome PayOwnActualUseTarget(ProgramSkillFrame frame)
    {
        var root = GetActiveProgramFrame(frame.Id);
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not ActualUseTargetWindowFrame parent)
            throw new InvalidOperationException("An actual target payment requires its direct owning window.");
        if (root.PaidOwnTarget is null)
        {
            var c = parent.Candidates[parent.CandidateIndex];
            var context = root.WindowContext!;
            if (!CanRunActualUseTarget(c, context) || !MountObserverCandidateMatches(root, c) || root.InstructionIndex != 1)
                throw new InvalidOperationException("The target payment lost its exact original use/candidate.");
            var before = _players[root.OwnerSeat].Hp;
            var receipt = new ProgramPaidOwnTargetReceipt(0, context.ActualUseTarget!,
                new(root.SkillId, GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId), root.GameplayHash, before, before - 1, 1);
            ReplaceRuntimeTop(root with { PaidOwnTarget = receipt, ReexecuteParticipantInstruction = true });
            AdvanceEventRulesAndQueueFact(new PaidOwnTargetHpEvent(root.Id, receipt));
            return new ProgramSkillHost(this).LoseHp(root.Id, root.SkillId, root.OwnerSeat, 1);
        }
        if (!PaidOwnTargetMatches(root, parent, true)) throw new InvalidOperationException("The resumed target payment lost its committed cost.");
        ApplyPaidOwnTarget(root);
        if (_winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(root.Id), "胜负已定，仅完成已付目标无效并取消未选择收益。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        return SkillProgramStepOutcome.Continue;
    }

    private void ApplyPaidOwnTarget(ProgramSkillFrame frame)
    {
        if (frame.PaidOwnTarget is not { Applied: false } paid) return;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ActualUseTargetWindowFrame parent || !PaidOwnTargetMatches(frame, parent, true))
            throw new InvalidOperationException("A paid target cannot apply to a different or completed use.");
        MarkCardEffectIneffective(paid.Use.CardUseFrameId, paid.Use.TargetSeat);
        ReplaceRuntimeFrame(frame.Id, frame with { PaidOwnTarget = paid with { Applied = true } });
        AdvanceEventRulesAndQueueFact(new PaidOwnTargetAppliedEvent(frame.Id, paid.Use.CardUseFrameId, paid.Use.ActionId,
            paid.Use.ActorSeat, paid.Use.TargetSeat, paid.Use.EffectiveKind, paid.Source, paid.GameplayHash));
        AdvanceEventRulesAndQueueFact(new ProgramCardEffectNullifiedEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
            frame.OwnerSeat, paid.Use.ActorSeat, paid.Use.CardUseFrameId, paid.Use.EffectiveKind));
    }
    // Paid cost is irrevocable. Qualification may cancel only the unchosen benefit.
    private void FinishPaidOwnTargetBeforeProgramCompletion(ProgramSkillFrame frame)
    {
        if (frame.PaidOwnTarget is { Applied: false }) ApplyPaidOwnTarget(frame);
    }

    private ProgramSkillFrame? ActualUseTargetObserverRoot(long useId)
    {
        var wi = _resolutionStack.FindIndex(f => f is ActualUseTargetWindowFrame w && w.ParentFrameId == useId);
        if (wi < 1 || _resolutionStack[wi] is not ActualUseTargetWindowFrame window ||
            _resolutionStack[wi - 1] is not CardUseFrame use || use.Id != useId || window.Candidates.Count != window.Contexts.Count ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            window.Contexts[window.CandidateIndex].ActualUseTarget is not { } identity || !MatchesActualUseTarget(identity)) return null;
        if (wi + 1 >= _resolutionStack.Count) return null;
        if (_resolutionStack[wi + 1] is not ProgramSkillFrame root || !MountObserverCandidateMatches(root, window.Candidates[window.CandidateIndex]) ||
            root.WindowContext?.ParentFrameId != window.Id || root.WindowContext.ActualUseTarget != identity) return null;
        if (wi + 2 == _resolutionStack.Count) return root;
        if (!PaidOwnTargetMatches(root, window, true)) return null;
        for (var i = wi + 2; i < _resolutionStack.Count; i++)
        {
            if (!PaidTargetObserverEdge(i)) return null;
            if (_resolutionStack[i] is DyingFrame d &&
                (IsPaidHandRepaymentProgramAlcoholRide(i, d) || IsPaidHandRepaymentRescueRide(i, d) || PolicyCounterspellVirtualAlcoholRide(i, d))) break;
        }
        return root;
    }
    // New paid roots opt in to exact Dying qualification and Jiushi's paused flip facts.
    // The old generic movement/Dying edge remains unchanged.
    private bool PaidTargetObserverEdge(int index)
    {
        if (PolicyCounterspellDyingFaceEdge(index)) return true;
        if (_resolutionStack[index - 1] is DyingFrame dying && _resolutionStack[index] is ProgramSkillFrame response)
            return PolicyCounterspellDyingProgramRidesOn(response, dying);
        return PreventionDrawObserverEdge(index);
    }
    private bool HasActualUseTargetObserver(long useId)
    {
        var window = _resolutionStack.OfType<ActualUseTargetWindowFrame>().LastOrDefault(w => w.ParentFrameId == useId);
        if (window is null) return false;
        if (_resolutionStack.LastOrDefault()?.Id == window.Id)
            return window.Candidates.Count == window.Contexts.Count && window.CandidateIndex >= 0 && window.CandidateIndex <= window.Candidates.Count &&
                (window.CandidateIndex == window.Candidates.Count || window.Contexts[window.CandidateIndex].ActualUseTarget is { } use && MatchesActualUseTarget(use));
        return ActualUseTargetObserverRoot(useId) is not null;
    }
    private bool IsPaidOwnTargetProgramDying() => ActiveDying is { ResumesProgramSkill: true } &&
        _resolutionStack.OfType<ActualUseTargetWindowFrame>().Any(w => ActualUseTargetObserverRoot(w.ParentFrameId) is { PaidOwnTarget: not null });

    private void AssertActualUseTargetPrograms()
    {
        foreach (var parent in _resolutionStack.OfType<ActualUseTargetWindowFrame>())
        {
            if (!HasActualUseTargetObserver(parent.ParentFrameId)) throw new InvalidOperationException("An actual target window lost its exact typed subtree.");
            foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.WindowContext?.ParentFrameId == parent.Id))
                if (root.PaidOwnTarget is not null && !PaidOwnTargetMatches(root, parent, true))
                    throw new InvalidOperationException("The paid target scalar cost receipt is not backed by its original HP fact.");
        }
    }
    private sealed partial class ProgramSkillHost : IPaidTargetEndingProgramHost
    {
        public SkillProgramStepOutcome PayHpThenNullifyOwnActualUseTarget(ProgramSkillFrame f) => engine.PayOwnActualUseTarget(f);
        public void ScheduleEarnedActualEndingBenefit(ProgramSkillFrame f, string skill, string binding) => engine.IssueEarnedActualEnding(f, skill, binding);
        public SkillProgramStepOutcome DrawLostHpThenOfferOwnedCardsUpTo(ProgramSkillFrame f) => engine.RunLostHpOwnedGift(f);
    }
}
