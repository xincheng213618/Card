namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DynamicDiscardDamageFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(root.DynamicDiscardDamage is not { } r || r.Stage==DynamicDiscardDamageStage.Choosing || !ValidDynamicDiscardDamagePayments(root))return false;
        var effect=ProgramInstructionResolver.Default.Resolve(root,_contentRegistry.GetSkill(root.SkillId).Program!).GetPausedInstruction(root.InstructionIndex).Effect;
        if(effect.Op!=SkillProgramEffectOp.DiscardTargetHpCardsAndDamage || r.InstructionIndex!=root.InstructionIndex)return false;
        if(r.Stage==DynamicDiscardDamageStage.Paid && root.PendingMovementContinuation is {SubjectSeat:var subject,BeforeCount:0,CoverageResultBind:null} && subject==root.OwnerSeat)
        {
            if(child is CardsMovedTriggerWindowFrame movement)return movement.Batch.ParentFrameId==root.Id &&
                (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId==root.Id) &&
                movement.Batch.OriginOwnerSeat==root.OwnerSeat && movement.Batch.OriginSkillId==root.SkillId && movement.Batch.OriginSkillInstanceId==root.SkillInstanceId &&
                movement.Batch.Movements.Count>0 && movement.Batch.Movements.All(m=>_cardMovements.Contains(m) && r.CardIds.Contains(m.CardId) &&
                    m.Sequence>r.PaymentSequenceBefore && m.Sequence<=r.PaymentSequenceAfter && m.Reason.Value==DynamicDiscardDamageReason);
            var removedLion=r.CardIds.Select((id,i)=>(id,i)).Any(x=>r.OriginalLocations[x.i]==CardLocation.Equipment(root.OwnerSeat) &&
                _cardMovements.Any(m=>m.Sequence>r.PaymentSequenceBefore && m.Sequence<=r.PaymentSequenceAfter && m.CardId==x.id && m.CardKind==CardKind.SilverLion));
            if(!removedLion)return false;
            if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id && hp.ResumeFrameId==root.Id &&
                hp.Continuation==PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind==HpChangeKind.Recovery && hp.Change.Amount==1 &&
                hp.Change.SourceSeat==root.OwnerSeat && hp.Change.TargetSeat==root.OwnerSeat;
            return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery,root) &&
                recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Amount==1 &&
                recovery.Attempt.SourceSeat==root.OwnerSeat && recovery.Attempt.TargetSeat==root.OwnerSeat &&
                recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value==DynamicDiscardDamageReason;
        }
        if(r.Stage==DynamicDiscardDamageStage.DamageIssued && root.AttackAttempt is { } attack && root.AttackReturn is not null)
        {
            if(child is DyingFrame {Continuation:DyingContinuationKind.AttackHpLoss} replaced)
                return PaidObserverAttackHpLossDyingMatches(root,replaced);
            if(child is BeforeDamageProgramWindowFrame before)return before.ParentFrameId==root.Id && before.Continuation==BeforeDamageProgramContinuation.Attack &&
                (before.ContinuationAttackResolutionId is null || before.ContinuationAttackResolutionId==root.Id) && before.SourceSeat==attack.SourceSeat &&
                (before.RedirectedTargetSeat??before.TargetSeat)==attack.TargetSeat && before.Amount>0 && before.Nature==attack.Nature;
            return child is DamageFrame damage && damage.Id==r.DamageFrameId && damage.ParentFrameId==root.Id && damage.SourceSeat==root.OwnerSeat &&
                damage.TargetSeat==r.ActualDamageTargetSeat && damage.Amount==attack.DamageAmount && damage.Nature==DamageNature.Normal;
        }
        if(r.Stage==DynamicDiscardDamageStage.DamageCompleted && child is HpChangedTriggerWindowFrame replacementHp)
            return replacementHp.ResumeFrameId==root.Id && replacementHp.Change.ParentFrameId==root.Id &&
                replacementHp.Continuation==PostEventContinuation.Program && replacementHp.Change.Kind==HpChangeKind.Loss &&
                CompleteProgramEventHistory().OfType<DamageReplacedWithHpLossEvent>().Any(e=>e.ParentResolutionId==root.Id &&
                    e.PolicyOwnerSeat==root.OwnerSeat && e.TargetSeat==replacementHp.Change.TargetSeat && e.Amount==replacementHp.Change.Amount);
        if(r.Stage==DynamicDiscardDamageStage.PenaltyIssued)
        {
            if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==root.Id && hp.Change.ParentFrameId==root.Id && hp.Continuation==PostEventContinuation.Program &&
                hp.Change.Kind==HpChangeKind.Loss && hp.Change.TargetSeat==root.OwnerSeat && hp.Change.Amount==1;
            return child is DyingFrame dying && dying.ParentFrameId==root.Id && dying.Continuation==DyingContinuationKind.ProgramSkill && dying.VictimSeat==root.OwnerSeat &&
                CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==root.Id && e.TargetSeat==root.OwnerSeat && e.Amount==1);
        }
        return false;
    }
    private ProgramSkillFrame? DynamicDiscardDamageObserverRoot(long? exactWindow=null)
    {
        for(var rootIndex=0;rootIndex+1<_resolutionStack.Count;rootIndex++)
        {
            if(_resolutionStack[rootIndex] is not ProgramSkillFrame root || !DynamicDiscardDamageFirstChild(root,_resolutionStack[rootIndex+1]))continue;
            if(exactWindow is { } id && !_resolutionStack.Skip(rootIndex+1).Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame or DamageTriggerWindowFrame))continue;
            var valid=true;
            for(var index=rootIndex+1;index<_resolutionStack.Count;index++)
            {
                if(index!=rootIndex+1 && !HalfHandPaidDamageObserverEdge(index) &&
                    !RequestedDeckBasicFrameRidesOn(_resolutionStack[index],_resolutionStack[index-1])){valid=false;break;}
                if(_resolutionStack[index] is DyingFrame d && (IsPaidHandRepaymentRescueRide(index,d) || IsPaidHandRepaymentProgramAlcoholRide(index,d) ||
                    PaidObserverDamageVirtualAlcoholRide(index,d) || PolicyCounterspellVirtualAlcoholRide(index,d) || IsRoundPricedPileAlcoholRide(index,d)))break;
            }
            if(valid)return root;
        }
        return null;
    }
    private bool HasDynamicDiscardDamageObserver(long window)=>DynamicDiscardDamageObserverRoot(window) is not null;
    private bool HasDynamicDiscardDamageAttackObserver(IDamageAttempt attack)=>DynamicDiscardDamageObserverRoot() is { } root &&
        root.Id==attack.ResolutionId && root.AttackAttempt is { } actual && actual.SourceSeat==attack.SourceSeat && actual.TargetSeat==attack.TargetSeat;
    private bool IsDynamicDiscardDamageProgramDying()=>ActiveDying is { } dying && DynamicDiscardDamageObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsDynamicDiscardNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(observer.AttackAttempt is not null || observer.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=observer.Id ||
            observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged))return false;
        var e=ProgramInstructionResolver.Default.Resolve(observer,_contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op==SkillProgramEffectOp.Damage && e.Amount==amount && e.ActorReference==source && e.DamageNature==nature &&
            target==(e.TargetReference is { } reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target)) &&
            DynamicDiscardDamageObserverRoot() is { } root && root.Id!=observer.Id && CurrentDamageAttempt?.ResolutionId==root.Id;
    }
    private bool TryAdvanceDynamicDiscardDamageSubtree()
    {
        if(_pendingDecision is not null || DynamicDiscardDamageObserverRoot() is null)return false;
        var top=_resolutionStack.LastOrDefault();
        if(top is ProgramSkillFrame {AttackAttempt:not null} attack)
        {
            if(attack.AttackReturn is null || CurrentDamageAttempt?.ResolutionId!=attack.Id || ActiveDying is not null ||
                _resolutionStack.Any(f=>f is DamageFrame d && d.ParentFrameId==attack.Id || f is BeforeDamageProgramWindowFrame b &&
                    (b.ContinuationAttackResolutionId??b.ParentFrameId)==attack.Id))return false;
            CompleteDamageAttack(new ProgramAttackHandle(this,attack.Id));AdvanceRulesAndPublishState();return true;
        }
        if(top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        {AdvanceRuntimeFrame(top.Id);AdvanceRulesAndPublishState();return true;}
        if(top is DeathFrame death){ContinueDeathResolution(death.Id);AdvanceRulesAndPublishState();return true;}
        return false;
    }
}
