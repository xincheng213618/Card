namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ProvenanceClaimTurnedOverEdge(ResolutionFrame child,ResolutionFrame parent)
    {
        if(child is ProgramLifecycleTriggerWindowFrame window && parent is ProgramSkillFrame { ProvenanceClaim:{Stage:ProgramProvenanceClaimStage.Flipping} } root)
            return window.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange && window.CharacterStateContinuation==CharacterStateContinuation.Program && window.ResumeProgramFrameId==root.Id && window.OwnerSeat==root.OwnerSeat && window.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp;
        if(child is not ProgramSkillFrame p || parent is not ProgramLifecycleTriggerWindowFrame lifecycle ||
            lifecycle.Continuation!=ProgramLifecycleContinuation.ResumeCharacterStateChange || lifecycle.CharacterStateContinuation!=CharacterStateContinuation.Program || lifecycle.Window is not (SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp) ||
            lifecycle.CandidateIndex<0 || lifecycle.CandidateIndex>=lifecycle.Candidates.Count ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==lifecycle.ResumeProgramFrameId) is not { ProvenanceClaim:{Stage:ProgramProvenanceClaimStage.Flipping} } original) return false;
        return MountObserverCandidateMatches(p,lifecycle.Candidates[lifecycle.CandidateIndex]) &&
            p.WindowContext is { } context && context.ParentFrameId==lifecycle.Id && context.Window==lifecycle.Window && context.TargetSeat==original.OwnerSeat;
    }
    private ProgramSkillFrame? ProvenanceClaimDamageObserverRoot(long damageId)
    {
        var start=_resolutionStack.FindIndex(f=>f.Id==damageId);
        if(start<0 || start+1>=_resolutionStack.Count || _resolutionStack[start] is not DamageTriggerWindowFrame damage || damage.CandidateIndex<0 || damage.CandidateIndex>=damage.Candidates.Count ||
            _resolutionStack[start+1] is not ProgramSkillFrame original || !MountObserverCandidateMatches(original,damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) || original.WindowContext?.ParentFrameId!=damage.Id) return null;
        ProgramSkillFrame? paid=null;
        for(var i=start+2;i<_resolutionStack.Count;i++)
        {
            if(_resolutionStack[i] is ProgramSkillFrame { ProvenanceClaim:{ } } claim)
            {
                AssertProvenanceClaim(claim);
                if(_resolutionStack[i-1] is not CardsMovedTriggerWindowFrame movement || movement.CandidateIndex<0 || movement.CandidateIndex>=movement.Candidates.Count ||
                    !MountObserverCandidateMatches(claim,movement.Candidates[movement.CandidateIndex]) || claim.WindowContext is not { Window:SkillProgramTriggerWindow.DiscardPileReceived } c || c.ParentFrameId!=movement.Id) return null;
                paid=claim;
            }
            if(_resolutionStack[i-1] is DyingFrame dying && IsPaidHandRepaymentProgramAlcoholRide(i-1,dying)) break;
            if(!PreventionDrawObserverEdge(i)) return null;
        }
        return paid;
    }
    private bool HasProvenanceClaimObserver(long damageId)=>ProvenanceClaimDamageObserverRoot(damageId) is not null;
    private bool IsProvenanceClaimProgramDying()=>ActiveDamageTrigger is { } damage && ActiveDying is {ResumesProgramSkill:true} dying &&
        ProvenanceClaimDamageObserverRoot(damage.Id) is { } root &&
        _resolutionStack.FindIndex(f=>f.Id==dying.FrameId)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
}
