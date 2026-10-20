using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30700, SkillProgramEffectOp.ResolveMouHanDangGongqi)]
    private bool CanOfferMouHanDangGongqi(ProgramTriggerLegalityContext gate) => gate.Context.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == gate.Candidate.OwnerSeat &&
        IsRecordedActualDiscardRecoveryPhase(p) && MouHanDangDiscardCards(p.ActorSeat).Length > 0;
    private bool MouHanDangPlayStartParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } c || c.OwnerSeat != f.OwnerSeat || f.ActivationId != f.TriggerId ||
            f.TriggerId != "discard-for-phase-responses") return false;
        return _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) switch
        {
            PlayPhaseStartingBoundaryFrame p when p.OwnerSeat == f.OwnerSeat && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } candidate => MountObserverCandidateMatches(f, candidate),
            ProgramLifecycleTriggerWindowFrame p when p.Window == c.Window && p.OwnerSeat == f.OwnerSeat && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count => MountObserverCandidateMatches(f, p.Candidates[p.CandidateIndex]),
            _ => false
        };
    }
}
