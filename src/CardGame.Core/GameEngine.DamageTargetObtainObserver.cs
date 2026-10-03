namespace CardGame.Core;
public sealed partial class GameEngine
{
    // Opt-in only to 2500's exact paid receipt and its actual first movement batch.
    private ProgramSkillFrame? PaidDamageTargetObtainObserverRoot(long windowId)
    {
        for(var i=1;i+1<_resolutionStack.Count;i++)
        {
            if(_resolutionStack[i] is not ProgramSkillFrame f || f.DamageTargetObtain is not {ObtainMovementSequence:{ } sequence} d ||
                !IsValidDamageTargetObtain(f) || d.DamageWindowId!=windowId ||
                _resolutionStack[i-1] is not DamageTriggerWindowFrame w || w.Id!=windowId || w.ParentFrameId!=d.DamageFrameId ||
                w.CandidateIndex<0 || w.CandidateIndex>=w.Candidates.Count || w.Candidates[w.CandidateIndex].ToProgramCandidate() is not { } candidate || !MountObserverCandidateMatches(f,candidate) ||
                _resolutionStack[i+1] is not CardsMovedTriggerWindowFrame first || f.PendingMovementContinuation is not {CoverageResultBind:null} pending || pending.SubjectSeat!=f.OwnerSeat ||
                first.Batch.ParentFrameId!=f.Id || first.Batch.AwaitingProgramFrameId!=f.Id || first.Batch.OriginSkillId!=f.SkillId ||
                first.Batch.OriginSkillInstanceId!=f.SkillInstanceId || first.Batch.OriginOwnerSeat!=f.OwnerSeat || first.Batch.Movements is not [var actual] || !_cardMovements.Contains(actual))continue;
            var exact=d.Stage==ProgramDamageTargetObtainStage.ObtainChildren
                ? actual.Sequence==sequence && actual.From.OwnerSeat==d.VictimSeat && DamageTargetMountZones.Contains(actual.From.Zone) && actual.Reason.Value==DamageTargetObtainReason
                : d.Stage==ProgramDamageTargetObtainStage.DrawChildren && d.Receipt is {IsEquipment:false} && actual.From==CardLocation.DrawPile && actual.To==CardLocation.Hand(d.VictimSeat) && actual.Reason.Value==DamageTargetBenefitReason;
            if(!exact)continue;
            var legal=true;
            for(var n=i+1;n<_resolutionStack.Count;n++)if(!PaidMountObserverEdge(n)){legal=false;break;}
            if(legal)return f;
        }
        return null;
    }
    private bool HasPaidDamageTargetObtainObserver(long id)=>PaidDamageTargetObtainObserverRoot(id) is not null;
    private bool IsPaidDamageTargetObtainDying()=>ActiveDamageTrigger is{ } w && ActiveDying is{ResumesProgramSkill:true} dying &&
        PaidDamageTargetObtainObserverRoot(w.Id) is not null && _resolutionStack.OfType<DyingFrame>().Any(f=>f.Id==dying.FrameId&&f.ParentFrameId==dying.ParentFrameId);
}
