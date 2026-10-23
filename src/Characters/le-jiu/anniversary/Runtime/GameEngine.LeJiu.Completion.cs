namespace CardGame.Core;

public sealed partial class GameEngine
{
    private LeJiuCuijinGrant? LeJiuGrantFor(CardUseFrame use, ProgramTriggerCandidate c, bool includeBound = false) =>
        (use.LeJiuCuijin ?? []).SingleOrDefault(g => g.Source.OwnerSeat == c.OwnerSeat && g.Source.SkillInstanceId == c.SkillInstanceId &&
            g.Hash == c.GameplayHash && ValidLeJiuGrant(use, g) && (includeBound || !ProgramEventHistory<LeJiuCuijinRewardBoundEvent>().Any(e => e.GrantFrameId == g.FrameId)));
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 44100)]
    private void CollectLeJiuCompletedUses(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseCompleted) return;
        result.RemoveAll(c => c.SkillId == AnniversaryLeJiuComposition.Skill && c.TriggerId == AnniversaryLeJiuComposition.Reward);
        if (_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { Step: ResolutionFrameStep.Completed, CausedDamage: false, LeJiuCuijin: { } grants } use) return;
        foreach (var g in grants.Where(g => ValidLeJiuGrant(use, g) && !ProgramEventHistory<LeJiuCuijinRewardBoundEvent>().Any(e => e.GrantFrameId == g.FrameId)))
            result.Add(new(g.Source.OwnerSeat, action.ActorSeat, g.Source.SkillId, AnniversaryLeJiuComposition.Reward, g.Hash, g.Source.SkillInstanceId!, 0,
                CreateCardActionProgramContext(action, window, 0, g.Source.OwnerSeat, action.ActorSeat, CaptureProgramTriggerFacts(_players[g.Source.OwnerSeat], action) with { CardUseCausedDamage = false })));
    }
    private bool LeJiuParent(ProgramSkillFrame f) => f.WindowContext is { } w &&
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is { } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])) && CreateCardActionProgramContext(parent, parent.Candidates[parent.CandidateIndex]) == w &&
        parent.ParentFrameId == w.CardUse?.ParentCardUseFrameId && parent.Action.ActionId == w.CardUse.CardActionId;
    private bool IsIssuedLeJiuCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w, bool includeBound)
    {
        if (c.SkillId != AnniversaryLeJiuComposition.Skill || c.BindingId != AnniversaryLeJiuComposition.Reward || w.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            w.OwnerSeat != c.OwnerSeat || LeJiuContextUse(w) is not { Step: ResolutionFrameStep.Completed, CausedDamage: false } use ||
            LeJiuGrantFor(use, c, includeBound) is null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1 ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent || parent.ParentFrameId != use.Id ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || ToSharedCandidate(parent.Candidates[parent.CandidateIndex]) != c) return false;
        return CreateCardActionProgramContext(parent, parent.Candidates[parent.CandidateIndex]) == w;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 44100)]
    private bool AdmitLeJiuIssuedCompletion(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => IsIssuedLeJiuCandidate(c, w, false);
    private static ProgramTriggerCandidate LeJiuCandidate(ProgramSkillFrame f) => new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0, f.WindowContext?.OccurrenceIndex ?? 0);
    private void BindLeJiuCuijin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.LeJiuCuijin is not null || f.WindowContext is not { } w ||
            !IsIssuedLeJiuCandidate(LeJiuCandidate(f), w, false) || LeJiuContextUse(w) is not { } use || LeJiuGrantFor(use, LeJiuCandidate(f)) is not { } grant)
            throw new InvalidOperationException("Cuijin must bind its one paid original whole-use no-damage result before drawing or damaging the user.");
        ReplaceRuntimeTop(f with { LeJiuCuijin = new(LeJiuSource(f), f.GameplayHash, w.ParentFrameId, use.Id, use.Action!.ActionId, use.SourceSeat, grant.Turn, 0, true, grant) });
        AdvanceEventRulesAndQueueFact(new LeJiuCuijinRewardBoundEvent(f.Id, grant.FrameId, use.Id, use.Action!.ActionId, f.OwnerSeat, use.SourceSeat));
    }
    private bool ValidLeJiuFrame(ProgramSkillFrame f)
    {
        if (f.LeJiuCuijin is not { } r || r.Source != LeJiuSource(f) || r.Hash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || f.WindowContext is not { } w || w.ParentFrameId != r.ParentId ||
            LeJiuContextUse(w) is not { } use || use.Id != r.UseId || use.Action!.ActionId != r.ActionId || use.SourceSeat != r.Actor ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !LeJiuParent(f)) return false;
        if (r.Reward) return f.InstructionIndex is >= 1 and <= 4 && r.Grant is { } grant &&
            IsIssuedLeJiuCandidate(LeJiuCandidate(f), w, true) && LeJiuGrantFor(use, LeJiuCandidate(f), true) == grant &&
            ProgramEventHistory<LeJiuCuijinRewardBoundEvent>().Count(e => e == new LeJiuCuijinRewardBoundEvent(f.Id, grant.FrameId, use.Id, r.ActionId, f.OwnerSeat, r.Actor)) == 1;
        return f.TriggerId == AnniversaryLeJiuComposition.Pay && w.Window == SkillProgramTriggerWindow.CardUseCommitted && r.Turn == _turnNumber && r.Before >= 0 && r.Before <= _movementSequence &&
            f.InstructionIndex is >= 1 and <= 4 && ProgramEventHistory<LeJiuCuijinQualifiedEvent>().Count(e => e == new LeJiuCuijinQualifiedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.UseId, r.ActionId, r.Actor, r.Turn, r.Before)) == 1 &&
            (r.Grant is null || ValidLeJiuGrant(use, r.Grant));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 44100)] private void AssertLeJiuFrame(ProgramSkillFrame f)
    {
        if (f.LeJiuCuijin is not null && !ValidLeJiuFrame(f)) throw new InvalidOperationException("Cuijin lost its original action, observer candidate, paid grant or whole-use return.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44100)]
    private bool ContinuePaidLeJiu(ProgramSkillFrame f) => f.LeJiuCuijin is { } r
        ? ValidLeJiuFrame(f) && (r.Reward || r.Grant is not null || LeJiuPaidCost(f) is not null)
        : f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && IsIssuedLeJiuCandidate(LeJiuCandidate(f), w, false);

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 44100)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 44100)]
    private bool LeJiuUndamagedUseConsequence(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.LeJiuCuijin is not { Reward: true } r || f.InstructionIndex != 3 ||
            f.AttackAttempt is not null || f.NativeDraw is not null || f.PendingMovementContinuation is not null ||
            _resolutionStack.LastOrDefault()?.Id != f.Id || CurrentDamageAttempt?.ResolutionId != r.UseId ||
            target != r.Actor || amount != 1 || source is not null || nature is not null || sourceLess || !ValidLeJiuFrame(f)) return false;

        // Instruction 2 is the ordinary native Draw. Only its typed recipient
        // return clears NativeDraw and advances this original frame to 3, after
        // all paid draw observers have returned. No second draw is issued here.
        // A request child is optional (there may be no eligible replacement);
        // when one was issued, retain its exact request/decision relationship.
        var attempts = ProgramEventHistory<NativeDrawAttemptStartedEvent>().Where(e => e.Context.ParentFrameId == f.Id).ToArray();
        if (attempts.Length == 0) return true;
        return attempts is [var started] && started.Context.RecipientSeat == f.OwnerSeat && started.Context.RequestedCount == 2 &&
            started.Context.Reason == new CardMoveReason($"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}") &&
            started.Return == new NativeDrawAttemptReturn(NativeDrawAttemptReturnKind.ProgramInstruction, 2, 0) &&
            ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == started.Context.AttemptFrameId &&
                e.ParentFrameId == f.Id && e.RecipientSeat == f.OwnerSeat && e.RequestedCount == 2) == 1;
    }
}
