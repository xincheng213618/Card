namespace CardGame.Core;
public sealed partial class GameEngine
{
    // This entire capability is opt-in to the exact paid 2300 producer and actual scalar record.
    // No source lifetime check: an already paid tail must also admit legal source loss/death cleanup.
    private ProgramSkillFrame? PaidDamageTargetMountObserverRoot(long damageWindowId)
    {
        for(var i=1;i+1<_resolutionStack.Count;i++)
        {
            if(_resolutionStack[i] is not ProgramSkillFrame root || root.DamageTargetMount is not {Receipt:{ } receipt} draft ||
                !IsValidDamageTargetMount(root) || draft.DamageWindowId!=damageWindowId ||
                _resolutionStack[i-1] is not DamageTriggerWindowFrame damage || damage.Id!=damageWindowId ||
                damage.ParentFrameId!=draft.DamageFrameId || damage.CandidateIndex<0 || damage.CandidateIndex>=damage.Candidates.Count ||
                damage.Candidates[damage.CandidateIndex].ToProgramCandidate() is not { } candidate || !MountObserverCandidateMatches(root,candidate) ||
                _resolutionStack[i+1] is not CardsMovedTriggerWindowFrame first ||
                root.PendingMovementContinuation is not {CoverageResultBind:null} pending || pending.SubjectSeat!=root.OwnerSeat ||
                first.Batch.ParentFrameId!=root.Id || first.Batch.AwaitingProgramFrameId!=root.Id ||
                first.Batch.OriginSkillId!=root.SkillId || first.Batch.OriginSkillInstanceId!=root.SkillInstanceId || first.Batch.OriginOwnerSeat!=root.OwnerSeat ||
                first.Batch.Movements.Count!=1) continue;
            var record=first.Batch.Movements[0];
            if(draft.ClaimIssued
                ? record.Sequence!=draft.ClaimMovementSequence || record.CardId!=receipt.CardId || record.From!=receipt.To || record.To!=CardLocation.Hand(root.OwnerSeat) || record.Reason.Value!="skill-program.damage-target-mount.claim"
                : record.Sequence!=receipt.MovementSequence || record.CardId!=receipt.CardId || record.From!=receipt.From || record.To!=receipt.To || record.Reason.Value!=receipt.Reason) continue;
            if(record.CardKind!=receipt.PrintedKind || !_cardMovements.Contains(record))continue;
            var legal=true;
            for(var n=i+1;n<_resolutionStack.Count;n++)
                if(!PaidMountObserverEdge(n)){legal=false;break;}
            if(legal)return root;
        }
        return null;
    }
    private static bool MountObserverCandidateMatches(ProgramSkillFrame p,ProgramTriggerCandidate c)=>
        p.OwnerSeat==c.OwnerSeat && p.SkillId==c.SkillId && p.TriggerId==c.BindingId && p.SkillInstanceId==c.SkillInstanceId &&
        p.GameplayHash==c.GameplayHash && p.WindowContext?.OccurrenceIndex==c.OccurrenceIndex;
    private bool PaidMountObserverEdge(int index)
    {
        var child=_resolutionStack[index];var parent=_resolutionStack[index-1];
        if(child is CardsMovedTriggerWindowFrame m && parent is ProgramSkillFrame p)
            return m.Batch.Id==m.Id && m.Batch.ParentFrameId==p.Id &&
                (m.Batch.AwaitingProgramFrameId is null || m.Batch.AwaitingProgramFrameId==p.Id) &&
                m.Batch.OriginSkillId==p.SkillId && m.Batch.OriginSkillInstanceId==p.SkillInstanceId && m.Batch.OriginOwnerSeat==p.OwnerSeat &&
                m.Batch.Movements.Count>0 && m.Batch.Movements.All(r=>_cardMovements.Contains(r));
        if(child is ProgramSkillFrame observer && parent is CardsMovedTriggerWindowFrame window)
            return window.CandidateIndex>=0 && window.CandidateIndex<window.Candidates.Count &&
                MountObserverCandidateMatches(observer,window.Candidates[window.CandidateIndex]) &&
                observer.WindowContext is{MovementBatch:{ } batch} c && c.ParentFrameId==window.Id && batch.Id==window.Batch.Id &&
                c.Window is SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived;
        if(child is HpChangedTriggerWindowFrame hp && parent is ProgramSkillFrame hpParent)
            return hp.ResumeFrameId==hpParent.Id && hp.Change.ParentFrameId==hpParent.Id;
        if(child is HpChangedTriggerWindowFrame peachHp && parent is CardUseFrame peachUse &&
            index>=2 && _resolutionStack[index-2] is DyingFrame peachDying)
            return IsAvailableBoundPeachRescueRide(index-2,peachDying) &&
                peachHp.ResumeFrameId==peachUse.Id && peachHp.Change.ParentFrameId==peachUse.Id &&
                peachHp.Change.Kind==HpChangeKind.Recovery && peachHp.Change.TargetSeat==peachDying.VictimSeat &&
                peachHp.Candidates.Count==peachHp.Contexts.Count && peachHp.Contexts.All(c=>
                    c.ParentFrameId==peachHp.Id && c.HpChange==peachHp.Change &&
                    c.Window is SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged);
        if(child is ProgramSkillFrame hpObserver && parent is HpChangedTriggerWindowFrame hpWindow)
            return hpWindow.CandidateIndex>=0 && hpWindow.CandidateIndex<hpWindow.Candidates.Count &&
                MountObserverCandidateMatches(hpObserver,hpWindow.Candidates[hpWindow.CandidateIndex]) &&
                hpObserver.WindowContext is{HpChange:{ } change} c && c.ParentFrameId==hpWindow.Id && change==hpWindow.Change;
        if(child is DyingFrame dying && parent is ProgramSkillFrame dyingParent)
            return dying.ResumesProgramSkill && dying.ParentFrameId==dyingParent.Id && ActiveDying?.FrameId==dying.Id;
        if(child is ProgramSkillFrame rescueProgram && parent is DyingFrame dyingWindow)
            return rescueProgram.WindowContext is{ } c && c.ParentFrameId==dyingWindow.Id &&
                c.Window is SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse;
        if(child is DeathFrame death && parent is DyingFrame dead)
            return death.ReturnKind==DeathReturnKind.Dying && death.ParentFrameId==dead.Id && death.VictimSeat==dead.VictimSeat;
        if(parent is DyingFrame peachParent && child is CardUseFrame)
            return IsAvailableBoundPeachRescueRide(index-1,peachParent);
        return false;
    }
    private bool HasPaidDamageTargetMountObserver(long damageWindowId)=>PaidDamageTargetMountObserverRoot(damageWindowId) is not null;
    private bool IsPaidDamageTargetMountDying()=>ActiveDamageTrigger is{ } damage && ActiveDying is{ResumesProgramSkill:true} dying &&
        PaidDamageTargetMountObserverRoot(damage.Id) is not null &&
        _resolutionStack.OfType<DyingFrame>().Any(f=>f.Id==dying.FrameId && f.ParentFrameId==dying.ParentFrameId);
}
