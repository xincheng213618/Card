namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouLuMengAwakened(int owner) => ProgramEventHistory<MouLuMengDujiangAwakenedEvent>().Any(e => e.OwnerSeat == owner && e.Source.SkillId == MouDujiang);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29201, SkillProgramEffectOp.ResolveMouLuMengDujiang)]
    private bool CanAwakenMouDujiang(ProgramTriggerLegalityContext c) => c.SkillId == MouDujiang && MouLuMengComposition.IsDujiang(c.Program) &&
        c.Context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && c.Context.SourceSeat == c.OwnerSeat && c.OwnerSeat == _currentSeat &&
        c.Owner.IsAlive && (c.Owner.Armor >= 3 || HasShenGuoJiaAwakeningCondition(c.OwnerSeat,c.SkillId,c.Candidate.BindingId)) && !MouLuMengAwakened(c.OwnerSeat) &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Preparation } p && p.ActorSeat == c.OwnerSeat;
    private bool MouDujiangRoot(ProgramSkillFrame f) => f.SkillId == MouDujiang && f.TriggerId == "armor-awakening" && f.ActivationId == f.TriggerId &&
        f.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c && c.OwnerSeat == f.OwnerSeat && c.SourceSeat == f.OwnerSeat &&
        _contentRegistry.GetSkill(f.SkillId).Program is { } p && p.GameplayHash == f.GameplayHash && MouLuMengComposition.IsDujiang(p) &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } parent &&
        parent.Window == c.Window && parent.OwnerSeat == f.OwnerSeat && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) &&
        _resolutionStack.FindIndex(x => x.Id == f.Id) is var i && i > 0 && _resolutionStack[i - 1].Id == parent.Id;
    private SkillProgramStepOutcome ResolveMouDujiang(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var phase = MouLuMengPhase();
        if (!MouDujiangRoot(f) || f.InstructionIndex != 1 || f.MouLuMengDujiang is not null || MouLuMengAwakened(f.OwnerSeat) ||
            !_players[f.OwnerSeat].IsAlive || _players[f.OwnerSeat].Armor < 3 && !HasShenGuoJiaAwakeningCondition(f.OwnerSeat,f.SkillId,f.TriggerId) || phase.Kind != ActualDiscardRecoveryPhaseKind.Preparation ||
            phase.ActorSeat != f.OwnerSeat || phase.ActualTurnOwnerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Dujiang requires its first actual Preparation with at least three actual Armor.");
        var receipt = new MouLuMengDujiangReceipt(MouLuMengSource(f), f.GameplayHash, phase);
        ReplaceRuntimeTop(f with { MouLuMengDujiang = receipt });
        AdvanceEventRulesAndQueueFact(new MouLuMengDujiangAwakenedEvent(f.Id, f.OwnerSeat, receipt.Source, receipt.GameplayHash, phase, _players[f.OwnerSeat].Armor));
        return SkillProgramStepOutcome.Continue;
    }
    private bool ValidMouDujiang(ProgramSkillFrame f) => MouDujiangRoot(f) && f.MouLuMengDujiang is { } r &&
        r.Source == MouLuMengSource(f) && r.GameplayHash == f.GameplayHash && r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation &&
        IsRecordedActualDiscardRecoveryPhase(r.Phase) && ProgramEventHistory<MouLuMengDujiangAwakenedEvent>().Count(e =>
            e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Phase == r.Phase && (e.Armor >= 3 || HasShenGuoJiaAwakeningReceipt(f))) == 1;
}
