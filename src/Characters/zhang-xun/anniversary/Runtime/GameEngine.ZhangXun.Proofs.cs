namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidZhangXunFollowup(ProgramSkillFrame f)
    {
        if (f.ZhangXunSuizheng is not { } r || f.SkillId != AnniversaryZhangXunComposition.Skill || f.TriggerId != AnniversaryZhangXunComposition.Followup ||
            f.ActivationId != f.TriggerId || f.InstructionIndex is < 1 or > 3 || f.SelectedCardIds.Count != 0 || r.Source != ZhangXunFrameSource(f) || r.Hash != f.GameplayHash ||
            !_contentRegistry.Skills.TryGetValue(f.SkillId, out var skill) || skill.Program?.GameplayHash != r.Hash ||
            r.ActualTurn != _turnNumber || r.RecipientSeat != _turnProgression.OwnerSeat || r.RecipientSeat != _currentSeat || _phase != TurnPhase.Play ||
            r.PhaseInstance != _cardUseDebitPhaseInstanceId || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding } context ||
            context.OwnerSeat != f.OwnerSeat || context.SourceSeat != r.RecipientSeat || context.ParentFrameId != r.ParentWindowId ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p => p.Id == r.ParentWindowId) is not { Window: SkillProgramTriggerWindow.PlayEnding } parent ||
            parent.OwnerSeat != r.RecipientSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) || context.OccurrenceIndex != parent.Candidates[parent.CandidateIndex].OccurrenceIndex ||
            r.Victims.Count == 0 || r.Victims.Select(v => v.TargetSeat).Distinct().Count() != r.Victims.Count || r.HistoryCutoff < 0) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (r.HistoryCutoff > history.Length || history.OfType<ZhangXunSuizhengLeaseEvent>().Where(e => e.FrameId == r.LeaseId).ToArray() is not [var lease] ||
            !ValidZhangXunLease(lease, history) || lease.Source.OwnerSeat != f.OwnerSeat || lease.Source.SkillId != f.SkillId || lease.Source.SkillInstanceId != f.SkillInstanceId ||
            lease.Hash != r.Hash || lease.RecipientSeat != r.RecipientSeat ||
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.OwnerSeat == f.OwnerSeat && e.Window == SkillProgramTriggerWindow.PlayEnding) != 1 ||
            history.OfType<ZhangXunSuizhengFollowupStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new ZhangXunSuizhengFollowupStartedEvent(f.Id, r.LeaseId, r.ParentWindowId, r.ActualTurn, r.RecipientSeat, r.PhaseInstance, r.HistoryCutoff, r.Victims.Count)) return false;
        var leaseIndex = Array.FindIndex(history, e => Equals(e, lease));
        var start = Array.FindIndex(history, leaseIndex + 1, e => e is TurnStartedEvent t && t.ActorSeat == r.RecipientSeat && t.TurnNumber > lease.IssuedTurn);
        if (start < 0 || start >= r.HistoryCutoff || history[start] is not TurnStartedEvent due || due.TurnNumber != r.ActualTurn ||
            history.Take(r.HistoryCutoff).Skip(start + 1).OfType<TurnEndedEvent>().Any(t => t.TurnNumber == r.ActualTurn && t.ActorSeat == r.RecipientSeat) ||
            history.OfType<ZhangXunSuizhengFollowupStartedEvent>().Count(e => e.LeaseId == r.LeaseId && e.ActualTurn == r.ActualTurn && e.PhaseInstance == r.PhaseInstance) != 1) return false;
        var leases = history.OfType<ZhangXunSuizhengLeaseEvent>().ToArray();
        if (context.OccurrenceIndex < 0 || context.OccurrenceIndex >= leases.Length || leases[context.OccurrenceIndex] != lease) return false;
        var victims = history.OfType<ZhangXunSuizhengVictimIssuedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Ordinal).ToArray();
        if (victims.Length != r.Victims.Count) return false;
        for (var i = 0; i < r.Victims.Count; i++)
        {
            var v = r.Victims[i];
            if (!IsValidPlayerSeat(v.TargetSeat) || v.TargetSeat == f.OwnerSeat || v.EventIndex <= start || v.EventIndex >= r.HistoryCutoff ||
                history[v.EventIndex] is not DamageAppliedEvent { Amount: > 0, SourceLess: false } damage || damage.SourceSeat != r.RecipientSeat || damage.TargetSeat != v.TargetSeat ||
                victims[i] != new ZhangXunSuizhengVictimIssuedEvent(f.Id, i, v.EventIndex, v.TargetSeat)) return false;
        }
        var choices = history.OfType<ZhangXunSuizhengTargetChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        return r.SelectedTarget is { } target
            ? r.Victims.Any(v => v.TargetSeat == target) && f.SelectedTargetSeats.SequenceEqual([target]) && choices is [var chosen] && chosen == new ZhangXunSuizhengTargetChosenEvent(f.Id, r.LeaseId, target)
            : f.InstructionIndex == 1 && f.SelectedTargetSeats.Count == 0 && choices.Length == 0;
    }
    private bool HasExactAnniversaryZhangXunFollowupSelection(ProgramSkillFrame f) => f.ZhangXunSuizheng is { SelectedTarget: not null } && ValidZhangXunFollowup(f);
    private bool HasAnniversaryZhangXunPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangXunSuizheng: { SelectedTarget: null } } f && f.Step == ResolutionFrameStep.AwaitingResponse &&
        p.PlayerSeat == f.OwnerSeat && ValidZhangXunFollowup(f) && AssistedChoicesEqual(p.Choices, ZhangXunFollowupChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 45200)] private void AssertZhangXunFollowup(ProgramSkillFrame f)
    {
        if (f.ZhangXunSuizheng is not { } r) return;
        if (!ValidZhangXunFollowup(f)) throw new InvalidOperationException("Suizheng lost its original lease, next actual turn/Play, actual damage witnesses or native Slash target.");
        if (r.SelectedTarget is null && _resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } p &&
            (p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, ZhangXunFollowupChoices(f)))) throw new InvalidOperationException("Suizheng's original issuer menu changed.");
    }
}
