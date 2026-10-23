namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhangXunFollowupAction = "anniversary-zhang-xun-followup";
    private CardConversionSource ZhangXunFrameSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryZhangXunHost(GameEngine engine) : IAnniversaryZhangXunHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp operation) => operation == SkillProgramEffectOp.ArmAnniversaryZhangXunSuizheng
            ? engine.ArmAnniversaryZhangXun(f) : engine.ChooseAnniversaryZhangXunSlash(f);
    }
    private SkillProgramStepOutcome ArmAnniversaryZhangXun(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != AnniversaryZhangXunComposition.Skill || f.TriggerId != AnniversaryZhangXunComposition.Arm || f.ActivationId != f.TriggerId || f.InstructionIndex != 2 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var recipient] || !IsValidPlayerSeat(recipient) || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is not TurnEndingBoundaryFrame parent || parent.TurnNumber != _turnNumber || parent.OwnerSeat != f.OwnerSeat ||
            parent.OwnerSeat != _currentSeat || context.OwnerSeat != f.OwnerSeat || context.SourceSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat ||
            parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate) ||
            context.OccurrenceIndex != candidate.OccurrenceIndex || context.Facts != (parent.Items[parent.ItemIndex].Facts ?? parent.Facts) ||
            ProgramEventHistory<ZhangXunSuizhengLeaseEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Suizheng requires its exact original Ending candidate and native selected recipient.");
        if (!_players[f.OwnerSeat].IsAlive || !_players[recipient].IsAlive) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ZhangXunSuizhengLeaseEvent(f.Id, ZhangXunFrameSource(f), f.GameplayHash, parent.Id, _turnNumber, recipient));
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramStepOutcome ChooseAnniversaryZhangXunSlash(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhangXunSuizheng is not null || f.SkillId != AnniversaryZhangXunComposition.Skill || f.TriggerId != AnniversaryZhangXunComposition.Followup || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } w)
            throw new InvalidOperationException("Suizheng requires its original unstarted issued Play-ending choice.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex);
        var due = ExactZhangXunCandidate(candidate, w);
        if (due is null || !IsIssuedAnniversaryZhangXunCandidate(candidate, w)) throw new InvalidOperationException("Suizheng lost the exact issuer, next actual turn or native Play-ending candidate.");
        var cutoff = CompleteProgramEventHistory().Count(); var victims = ZhangXunVictims(due, cutoff);
        if (victims.Count == 0) { FinishProgramSkill(f, false); return SkillProgramStepOutcome.AwaitChild; }
        var r = new ZhangXunSuizhengFollowupReceipt(ZhangXunFrameSource(f), f.GameplayHash, due.Lease.FrameId, w.ParentFrameId, _turnNumber,
            due.Lease.RecipientSeat, _cardUseDebitPhaseInstanceId, cutoff) { Victims = victims };
        ReplaceRuntimeTop(f = f with { ZhangXunSuizheng = r });
        AdvanceEventRulesAndQueueFact(new ZhangXunSuizhengFollowupStartedEvent(f.Id, r.LeaseId, r.ParentWindowId, r.ActualTurn, r.RecipientSeat, r.PhaseInstance, cutoff, victims.Count));
        foreach (var (victim, index) in victims.Select((v, i) => (v, i))) AdvanceEventRulesAndQueueFact(new ZhangXunSuizhengVictimIssuedEvent(f.Id, index, victim.EventIndex, victim.TargetSeat));
        PublishZhangXunFollowup(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ZhangXunFollowupChoices(ProgramSkillFrame f) => Array.AsReadOnly(f.ZhangXunSuizheng!.Victims
        .Where(v => LegalZhangXunFollowupTarget(f.OwnerSeat, v.TargetSeat)).Select(v => new PromptChoice(new($"zhang-xun.{f.Id}.{v.TargetSeat}"),
            "视为对" + _players[v.TargetSeat].Name + "使用一张杀", [], [v.TargetSeat], new Dictionary<string, string> { ["program-action"] = ZhangXunFollowupAction })).ToArray());
    private void PublishZhangXunFollowup(ProgramSkillFrame f)
    {
        var choices = ZhangXunFollowupChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || choices.Count == 0) { FinishProgramSkill(f, false); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "随征：选择本回合受益角色实际造成过伤害的角色。", f.ZhangXunSuizheng!.RecipientSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(ZhangXunFollowupAction)] private void AnswerZhangXunFollowup(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangXunSuizheng: { SelectedTarget: null } r } f || !ValidZhangXunFollowup(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], ZhangXunFollowupChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Suizheng rejects another issuer's turn, damage victim or stale native target choice.");
        var target = choice.Targets.Single(); ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, SelectedTargetSeats = Array.AsReadOnly(new[] { target }), ZhangXunSuizheng = r with { SelectedTarget = target } });
        AdvanceEventRulesAndQueueFact(new ZhangXunSuizhengTargetChosenEvent(f.Id, r.LeaseId, target));
        // Continue the configured UseVirtualSlash instruction; it owns the real
        // CardUse/response/damage chain and typed Program parent return.
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ChooseAnniversaryZhangXunSuizhengSlash)] private PromptChoice ZhangXunFollowupAi(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45200)] private bool KeepIssuedZhangXunFollowup(ProgramSkillFrame f)
    {
        if (f.ZhangXunSuizheng is not null) return ValidZhangXunFollowup(f);
        return f.SkillId == AnniversaryZhangXunComposition.Skill && f.TriggerId == AnniversaryZhangXunComposition.Followup && f.InstructionIndex == 0 && f.WindowContext is { } w &&
            IsIssuedAnniversaryZhangXunCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, w.OccurrenceIndex), w);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 45200)] private bool KeepZhangXunAlreadyUsedSlash(ProgramSkillFrame f) => f.ZhangXunSuizheng is { SelectedTarget: not null } && ValidZhangXunFollowup(f);
}
