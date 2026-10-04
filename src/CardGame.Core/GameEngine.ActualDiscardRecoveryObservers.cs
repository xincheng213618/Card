namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ActualEquipmentOrDiscardFirstObserver(int index,ProgramSkillFrame root)
    {
        var child=_resolutionStack[index+1];long before,after;
        if(root.CapturedEquipmentDraw is { } equipment)
        {
            if(equipment.Stage==CapturedEquipmentDrawStage.Placed){before=equipment.SequenceBefore;after=equipment.SequenceAfter;}
            else if(equipment.Stage==CapturedEquipmentDrawStage.Drawn){before=equipment.DrawSequenceBefore;after=equipment.DrawSequenceAfter;}
            else return false;
        }
        else if(root.ActualDiscardRecovery is { Stage:ActualDiscardRecoveryStage.ReturnPaid } returned)
        {before=returned.ReturnMovementSequence-1;after=returned.ReturnMovementSequence;}
        else if(root.ActualDiscardRecovery is { Stage:ActualDiscardRecoveryStage.ClaimPaid } claim)
        {before=claim.ClaimSequenceBefore;after=claim.ClaimSequenceAfter;}
        else return false;
        if(root.PendingMovementContinuation is not { SubjectSeat:var subject,BeforeCount:0,CoverageResultBind:null } ||subject!=root.OwnerSeat)return false;
        if(child is CardsMovedTriggerWindowFrame movement)
            return movement.Batch.ParentFrameId==root.Id &&(movement.Batch.AwaitingProgramFrameId is null ||movement.Batch.AwaitingProgramFrameId==root.Id) &&
                movement.Batch.OriginSkillId==root.SkillId &&movement.Batch.OriginSkillInstanceId==root.SkillInstanceId &&movement.Batch.OriginOwnerSeat==root.OwnerSeat &&
                movement.Batch.Movements.Count>0 &&movement.Batch.Movements.All(m=>_cardMovements.Contains(m) &&m.Sequence>before &&m.Sequence<=after);
        if(root.CapturedEquipmentDraw is not { Stage:CapturedEquipmentDrawStage.Placed })return false;
        var removals=_cardMovements.Where(m=>m.Sequence>before &&m.Sequence<=after &&m.CardKind==CardKind.SilverLion &&
            m.From.Zone==CardZoneKind.Equipment &&m.From.OwnerSeat is not null &&m.To!=m.From).ToArray();
        if(child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery,root) &&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion &&recovery.Attempt.Amount==1 &&
                recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat &&removals.Any(m=>m.From.OwnerSeat==recovery.Attempt.TargetSeat &&
                    recovery.Attempt.Completion.MoveReason==m.Reason);
        return child is HpChangedTriggerWindowFrame hp &&hp.Change.ParentFrameId==root.Id &&hp.ResumeFrameId==root.Id &&
            hp.Continuation==PostEventContinuation.AwaitedProgramMovement &&hp.Change.Kind==HpChangeKind.Recovery &&hp.Change.Amount==1 &&
            hp.Change.SourceSeat==hp.Change.TargetSeat &&removals.Any(m=>m.From.OwnerSeat==hp.Change.TargetSeat);
    }

    private ProgramSkillFrame? ActualEquipmentOrDiscardObserverRoot(long id)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==id);
        if(index<0 ||_resolutionStack[index] is not ProgramSkillFrame root ||root.CapturedEquipmentDraw is null &&root.ActualDiscardRecovery is null)return null;
        AssertCapturedEquipmentAndDraw(root);AssertActualDiscardRecovery(root);
        if(index==_resolutionStack.Count-1)return root;
        if(!ActualEquipmentOrDiscardFirstObserver(index,root))return null;
        for(var child=index+1;child<_resolutionStack.Count;child++)
        {
            if(child>index+1 &&!HalfHandPaidDamageObserverEdge(child))return null;
            if(_resolutionStack[child] is DyingFrame dying &&(IsPaidHandRepaymentRescueRide(child,dying) ||
                IsPaidHandRepaymentProgramAlcoholRide(child,dying) ||PolicyCounterspellVirtualAlcoholRide(child,dying) ||
                PaidObserverDamageVirtualAlcoholRide(child,dying)))break;
        }
        return root;
    }

    private bool IsActualEquipmentOrDiscardProgramDying()=>ActiveDying is { } dying &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f=>(f.CapturedEquipmentDraw is not null ||f.ActualDiscardRecovery is not null) &&
            ActualEquipmentOrDiscardObserverRoot(f.Id) is not null &&
            _resolutionStack.FindIndex(d=>d.Id==dying.Id)>_resolutionStack.FindIndex(r=>r.Id==f.Id));

    private bool HasActualDiscardRecoveryDamageObserver(long damageWindowId)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==damageWindowId);
        // Native damage newly issued by a paid root's gain/HP observer is
        // proved from that receipt through the entire exact current subtree.
        if(index>=0 &&_resolutionStack[index] is DamageTriggerWindowFrame &&
            _resolutionStack.Take(index).OfType<ProgramSkillFrame>().Any(f=>(f.ActualDiscardRecovery is not null ||f.CapturedEquipmentDraw is not null) &&
                ActualEquipmentOrDiscardObserverRoot(f.Id) is not null))return true;
        if(index<0 ||_resolutionStack[index] is not DamageTriggerWindowFrame damage ||damage.CandidateIndex<0 ||damage.CandidateIndex>=damage.Candidates.Count ||
            index+1>=_resolutionStack.Count ||_resolutionStack[index+1] is not ProgramSkillFrame producer ||
            !MountObserverCandidateMatches(producer,damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
            producer.WindowContext?.ParentFrameId!=damage.Id)return false;
        foreach(var root in _resolutionStack.Skip(index+2).OfType<ProgramSkillFrame>().Where(f=>f.ActualDiscardRecovery is not null))
        {
            if(ActualEquipmentOrDiscardObserverRoot(root.Id) is null)continue;
            var end=_resolutionStack.FindIndex(f=>f.Id==root.Id);var okay=true;
            for(var edge=index+2;edge<=end;edge++)if(!HalfHandPaidDamageObserverEdge(edge)){okay=false;break;}
            if(okay)return true;
        }
        return false;
    }

    private bool HasActualDiscardRecoveryBeforeDamageObserver(long beforeDamageId)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==beforeDamageId);
        if(index>=0 &&_resolutionStack[index] is BeforeDamageProgramWindowFrame &&
            _resolutionStack.Take(index).OfType<ProgramSkillFrame>().Any(f=>(f.ActualDiscardRecovery is not null ||f.CapturedEquipmentDraw is not null) &&
                ActualEquipmentOrDiscardObserverRoot(f.Id) is not null))return true;
        if(index<0 ||_resolutionStack[index] is not BeforeDamageProgramWindowFrame before ||before.CandidateIndex<0 ||before.CandidateIndex>=before.Candidates.Count ||
            index+1>=_resolutionStack.Count ||_resolutionStack[index+1] is not ProgramSkillFrame producer ||
            !MountObserverCandidateMatches(producer,before.Candidates[before.CandidateIndex].Candidate) ||
            producer.WindowContext?.ParentFrameId!=before.Id)return false;
        foreach(var root in _resolutionStack.Skip(index+2).OfType<ProgramSkillFrame>().Where(f=>f.ActualDiscardRecovery is not null))
        {
            if(ActualEquipmentOrDiscardObserverRoot(root.Id) is null)continue;
            var end=_resolutionStack.FindIndex(f=>f.Id==root.Id);var okay=true;
            for(var edge=index+2;edge<=end;edge++)if(!HalfHandPaidDamageObserverEdge(edge)){okay=false;break;}
            if(okay)return true;
        }
        return false;
    }

    private bool AllowsActualEquipmentOrDiscardNestedDamage(ProgramSkillFrame observer,int target,int amount,
        ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(observer.AttackAttempt is not null ||observer.InstructionIndex<1 ||_resolutionStack.LastOrDefault()?.Id!=observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged) ||CurrentDamageAttempt is not { } original)return false;
        var effect=ProgramInstructionResolver.Default.Resolve(observer,_contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        if(effect.Op!=SkillProgramEffectOp.Damage ||amount!=effect.Amount ||source!=effect.ActorReference ||nature!=effect.DamageNature ||
            target!=(effect.TargetReference is { } reference ? ResolveProgramParticipant(observer,reference) : ResolveProgramEffectTarget(observer,effect.Target)))return false;
        var originalIndex=_resolutionStack.FindIndex(f=>f.Id==original.ResolutionId);
        return originalIndex>=0 &&_resolutionStack.Skip(originalIndex+1).OfType<ProgramSkillFrame>().Any(f=>
            (f.ActualDiscardRecovery is not null ||f.CapturedEquipmentDraw is not null) &&ActualEquipmentOrDiscardObserverRoot(f.Id) is not null);
    }

    private void AssertActualEquipmentOrDiscardPrograms()
    {
        foreach(var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.ActualDiscardRecovery is not null ||f.CapturedEquipmentDraw is not null))
            if(ActualEquipmentOrDiscardObserverRoot(root.Id) is null)
                throw new InvalidOperationException("Actual equipment/discard recovery lost its exact paid receipt and typed descendant subtree.");
    }

    private bool IsActualEquipmentOrDiscardMovement(ProgramSkillFrame f,SkillProgramEffect? op,ProgramMovementContinuation pending)=>
        pending.SubjectSeat==f.OwnerSeat &&pending.BeforeCount==0 &&pending.CoverageResultBind is null &&
        (op?.Op==SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw &&f.CapturedEquipmentDraw is not null ||
         op?.Op==SkillProgramEffectOp.RestoreActualDiscardBatch &&f.ActualDiscardRecovery is not null);
}
