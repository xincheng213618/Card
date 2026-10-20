using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int BeiMiHuActualCards(int owner)=>checked(CompleteProgramEventHistory().OfType<BeiMiHuGujuDrawPaidEvent>().Where(e=>e.Source.OwnerSeat==owner).Sum(e=>e.Actual));
    private bool CanDrawAfterKuiDamage(int owner,SkillProgramTrigger trigger,int target)=>BeiMiHuOperation(trigger)!=SkillProgramEffectOp.DrawAfterActualKuiDamage ||
        IsValidPlayerSeat(target) && _players[target].Markers.GetValueOrDefault(PlayerMarkerKind.Kui)>0;
    private SkillProgramStepOutcome BeginBeiMiHuGuju(ProgramSkillFrame f)
    {
        var c=f.WindowContext;var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(c is not{Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0,DamageFrameId:{}damage} || index<1 ||
            _resolutionStack[index-1] is not DamageTriggerWindowFrame p || p.Id!=c.ParentFrameId || p.ParentFrameId!=damage || p.CandidateIndex<0 || p.CandidateIndex>=p.Candidates.Count ||
            !MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex].ToProgramCandidate()) || !CanDrawAfterKuiDamage(f.OwnerSeat,GetProgramTrigger(f),p.TargetSeat) ||
            CompleteProgramEventHistory().OfType<BeiMiHuGujuDrawPaidEvent>().Any(e=>e.DamageWindowId==p.Id && e.Source.OwnerSeat==f.OwnerSeat))throw new InvalidOperationException("Guju lost one actual applied damage on a public marked recipient.");
        var before=_movementSequence;var r=NewBeiMiHuReceipt(f,"draw-children") with{DamageParent=damage,DamageTarget=p.TargetSeat,Before=before,After=before};
        ReplaceRuntimeTop(f=f with{BeiMiHu=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});DrawProgramCards(f.Id,f.OwnerSeat,1,null,null,SkillProgramCardSetVisibility.Private,new(BeiMiHuDrawReason));
        f=GetActiveProgramFrame(f.Id);var actual=_cardMovements.Count(m=>m.Sequence>before && m.Sequence<=_movementSequence && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==BeiMiHuDrawReason);
        ReplaceRuntimeTop(f=f with{BeiMiHu=r with{After=_movementSequence,ActualDraw=actual}});
        AdvanceEventRulesAndQueueFact(new BeiMiHuGujuDrawPaidEvent(f.Id,r.Source,r.GameplayHash,p.Id,damage,p.TargetSeat,r.Turn,before,_movementSequence,actual));
        if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 170)]
    private bool IsBeiMiHuMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation pending)=>effect?.Op==SkillProgramEffectOp.DrawAfterActualKuiDamage && f.BeiMiHu is{Stage:"draw-children"} && pending.SubjectSeat==f.OwnerSeat && pending.BeforeCount==0 && pending.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,23202)]
    private bool ReturnBeiMiHuMovement(ProgramSkillFrame f)
    {
        if(f.BeiMiHu is null)return false;if(f.PendingMovementContinuation is not{}p || !IsBeiMiHuMovement(f,BeiMiHuPausedEffect(f),p))throw new InvalidOperationException("Guju received a foreign movement return.");
        AssertBeiMiHu(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;FinishBeiMiHu(f);return true;
    }
}
