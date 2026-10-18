namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouGuquDrawReason = "skill-program.mou-zhou-yu.guqu.draw";
    [ProgramCapability]
    private sealed class MouGuquCapability(GameEngine engine) : IMouZhouYuGuquHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginMouGuqu(frame); }
    private static CardConversionSource MouGuquFrameSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29021, SkillProgramEffectOp.ResolveMouZhouYuGuqu)]
    private bool CanOfferMouGuqu(ProgramTriggerLegalityContext gate) => IsIssuedMouGuquCandidate(gate.Candidate, gate.Context);
    private bool IsIssuedMouGuquCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        _players[c.OwnerSeat].IsAlive && MouGuquCompletedClaim(c, context) is { } q &&
        !ProgramEventHistory<GuquStartedEvent>().Any(e => e.Claim.Source == q.Source && e.Claim.ActionId == q.ActionId);
    private void CollectIssuedMouGuquCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasMouGuquCapability || window != SkillProgramTriggerWindow.CardUseCompleted) return;
        var parent = _resolutionStack.LastOrDefault();
        if (parent?.GuquClaims is not { } claims) return;
        foreach (var q in claims.Where(q => q.ActionId == action.ActionId && q.ParentFrameId == parent.Id &&
            _players[q.Source.OwnerSeat].IsAlive && ValidMouGuquClaim(q, action)))
        {
            if (result.Any(c => c.OwnerSeat == q.Source.OwnerSeat && c.SkillId == q.Source.SkillId && c.SkillInstanceId == q.Source.SkillInstanceId && c.TriggerId == q.Source.BindingId)) continue;
            var t = _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId);
            result.Add(new(q.Source.OwnerSeat, -1, q.Source.SkillId, q.Source.BindingId, q.GameplayHash, q.Source.SkillInstanceId, t.Priority,
                CreateCardActionProgramContext(action, window, 0, q.Source.OwnerSeat, -1, CaptureProgramTriggerFacts(_players[q.Source.OwnerSeat], action))));
        }
    }
    private bool HasIssuedMouGuquResponse(CardActionContext action) => HasMouGuquCapability && _resolutionStack.Any(f =>
        f.GuquClaims?.Any(q => q.ActionId == action.ActionId && q.ParentFrameId == f.Id && _players[q.Source.OwnerSeat].IsAlive && ValidMouGuquClaim(q, action)) == true);
    private SkillProgramStepOutcome BeginMouGuqu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, f.WindowContext?.OccurrenceIndex ?? 0);
        if (f.Guqu is not null || f.SkillId != MouGuquSkill || f.ActivationId != MouGuquBinding || f.TriggerId != MouGuquBinding ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !IsIssuedMouGuquCandidate(candidate, context) || MouGuquCompletedClaim(candidate, context) is not { } q ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            !DesignatedExtraTargetCandidateMatches(candidate, context, w))
            throw new InvalidOperationException("Guqu requires its original source's marked true Use claim and exact native completion window.");
        ReplaceRuntimeTop(f = f with { Guqu = new(q, w.Id, GuquStage.Drawing) });
        AdvanceEventRulesAndQueueFact(new GuquStartedEvent(f.Id, q, w.Id));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 的【顾曲】摸两张牌。", f.OwnerSeat);
        ContinueMouGuqu(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhouYuGuqu)]
    private void ReturnMouGuquDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.Guqu is not { Stage: GuquStage.Drawing } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != 2 ||
            ProgramEventHistory<GuquDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Guqu cannot repeat a completed native draw or replace its actual-material invoice.");
        ReplaceRuntimeTop(f = f with { Guqu = r with { Stage = GuquStage.DrawChildren, DrawOutcome = o }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new GuquDrawReturnedEvent(f.Id, 2, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId));
        ContinueMouGuqu(f.Id);
    }
    private void ContinueMouGuqu(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { Guqu: { } r } f || f.Id != id) return;
        AssertMouGuqu(f);
        if (r.Stage == GuquStage.Drawing)
        { if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(MouGuquDrawReason)); return; }
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id)) return;
        AdvanceEventRulesAndQueueFact(new GuquCompletedEvent(f.Id, r.Claim.ActionId));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { Guqu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29021)]
    private bool ResumeMouGuqu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Guqu: not null } f || f.Id != id) return false; ContinueMouGuqu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29021)]
    private bool ReturnMouGuquMovement(ProgramSkillFrame f)
    { if (f.Guqu is null || f.PendingMovementContinuation is null) return false; ContinueMouGuqu(f.Id); return true; }
}
