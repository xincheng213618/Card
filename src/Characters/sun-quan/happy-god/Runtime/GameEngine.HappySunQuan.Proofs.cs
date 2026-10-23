namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,40940)] private void RequireHappySunQuan(ProgramSkillFrame f)
    {
        if(f.HappySunQuan is not{}r)return;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        var initialTarget=r.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu?f.OwnerSeat:r.Target;
        if(f.InstructionIndex!=1||r.Source!=HappySunQuanSource(f)||r.GameplayHash!=f.GameplayHash||!HappySunQuanComposition.Owns(r.Operation)||!Enum.IsDefined(r.Stage)||
            r.ParentId!=f.WindowContext?.ParentFrameId||r.ActualTurn!=_turnNumber||r.Before<0||r.After<r.Before||r.After>_movementSequence||index<1||_resolutionStack[index-1].Id!=r.ParentId||
            r.Targets.Distinct().Count()!=r.Targets.Count||r.Targets.Any(s=>!IsValidPlayerSeat(s))||r.Index<0||r.Index>r.Targets.Count||
            ProgramEventHistory<HappySunQuanStartedEvent>().Count(e=>e==new HappySunQuanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentId,r.ActualTurn,r.ActionId,initialTarget,r.Amount))!=1)
            throw new InvalidOperationException("Happy Sun Quan lost his original frame, source instance, parent or paid cursor.");
        if(r.Operation==SkillProgramEffectOp.GrantHappySunQuanLearning)
        {
            if(_resolutionStack[index-1] is not PlayPhaseStartingBoundaryFrame p||p.OwnerSeat!=f.OwnerSeat||p.ItemIndex<0||p.ItemIndex>=p.Items.Count||
                p.Items[p.ItemIndex].Candidate is not{}c||!MountObserverCandidateMatches(f,c)||r.Targets.Count>2||r.Targets.Contains(f.OwnerSeat)||
                r.Targets.Any(s=>ProgramEventHistory<HappyXueGrantEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Target==s&&e.After==e.Before+1)!=1))
                throw new InvalidOperationException("Quanxue lost the actual own Play boundary and issued distinct other recipients.");
        }
        else if(r.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu)
        {
            if(_resolutionStack[index-1] is not ProgramCardTriggerWindowFrame p||p.Action.ActionId!=r.ActionId||p.Action.Type!=CardActionType.Use||p.Action.ActorSeat!=f.OwnerSeat||!IsSlashCard(p.Action.EffectiveKind)||
                p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,ToSharedCandidate(p.Candidates[p.CandidateIndex]))||
                r.Targets.Where((s,i)=>!p.Action.EffectiveDesignatedTargetSeats.Contains(s)||ProgramEventHistory<HappyShehuTargetEvent>().Count(e=>e.FrameId==f.Id&&e.Index==i&&e.ActionId==r.ActionId&&e.Target==s&&e.MarkerCount>0)!=1).Any())
                throw new InvalidOperationException("Shehu lost the exact real Slash's issued marked target group.");
            foreach(var payment in r.Payments)
            {
                if(payment.Index<0||payment.Index>=r.Targets.Count||payment.Target!=r.Targets[payment.Index]||payment.Index>r.Index||payment.Before<0||payment.After<=payment.Before||payment.After>_movementSequence||
                    payment.Movements.Count==0||payment.Movements.Any(m=>m.CardId!=payment.CardId||m.From!=CardLocation.Hand(payment.Target)||m.Sequence<=payment.Before||m.Sequence>payment.After||m.Reason.Value!=HappyShehuReason||
                        m.To!=CardLocation.DiscardPile&&!IsRedirectedPaidDiscardMovement(m,CardLocation.Hand(payment.Target),new(HappyShehuReason))||_cardMovements.Count(a=>a==m)!=1)||
                    ProgramEventHistory<HappySunQuanMovementEvent>().Count(e=>e==new HappySunQuanMovementEvent(f.Id,payment.Index,payment.Before,payment.After,payment.Movements.Count))!=1)
                    throw new InvalidOperationException("Shehu lost a frozen original opaque-Hand payment.");
            }
            for(var i=0;i<r.Index;i++)if(r.Payments.Count(p=>p.Index==i)+ProgramEventHistory<HappyShehuSkippedEvent>().Count(e=>e==new HappyShehuSkippedEvent(f.Id,i,r.Targets[i]))!=1)
                throw new InvalidOperationException("Shehu lost an exactly once previous target result.");
        }
        else
        {
            if(_resolutionStack[index-1] is not ProgramLifecycleTriggerWindowFrame p||!IsHappyLearningStart(p)||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex])||p.OwnerSeat!=r.Target||r.ActionId!=0)
                throw new InvalidOperationException("Learning/Dingli lost the actual holder's native turn-start window.");
            if(r.Operation==SkillProgramEffectOp.ResolveHappySunQuanLearning)
            {
                if(f.OwnerSeat!=r.Target||p.CandidateIndex!=0||r.Amount!=p.HappyLearningStart!.Grants.Sum(g=>g.After)||
                    ProgramEventHistory<HappyLearningRemovedEvent>().Count(e=>e==new HappyLearningRemovedEvent(f.Id,p.Id,r.ActualTurn,f.OwnerSeat,r.Amount))!=1||
                    r.Branch>0&&ProgramEventHistory<HappyLearningChoiceEvent>().Count(e=>e==new HappyLearningChoiceEvent(f.Id,p.Id,r.ActualTurn,f.OwnerSeat,r.Branch))!=1)
                    throw new InvalidOperationException("Learning lost its paid removal and original holder's selected obligation.");
            }
            else
            {
                if(p.CandidateIndex==0||f.OwnerSeat==r.Target||r.Amount is <1 or >2||r.Branch is not(1 or 2)||
                    ProgramEventHistory<HappyDingliBenefitEvent>().Count(e=>e.FrameId==f.Id&&e.WindowId==p.Id&&e.OtherSeat==r.Target&&e.Branch==r.Branch&&e.Amount==r.Amount&&
                        (r.Branch==1?e.OtherHp>=e.OwnerHp&&r.Amount==1:e.OtherHp<e.OwnerHp&&r.Amount==Math.Min(2,e.OwnerHp-e.OtherHp)))!=1)
                    throw new InvalidOperationException("Dingli lost its accepted round-limited, frozen health comparison.");
            }
        }
        if(r.Paid.Any(m=>m.Sequence<=r.Before||m.Sequence>r.After||_cardMovements.Count(a=>a==m)!=1))throw new InvalidOperationException("Happy Sun Quan lost a real current movement batch.");
        if(f.PendingMovementContinuation is not null&&ProgramEventHistory<HappySunQuanMovementEvent>().Count(e=>e==new HappySunQuanMovementEvent(f.Id,r.Index,r.Before,r.After,r.Paid.Count))!=1)
            throw new InvalidOperationException("Happy Sun Quan lost his current once-paid movement invoice.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,40940)]
    private bool HappySunQuanMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)
    {
        if(f.HappySunQuan is not{}r||effect?.Op!=r.Operation||pending!=f.PendingMovementContinuation||pending.SubjectSeat!=f.OwnerSeat||pending.BeforeCount!=0||pending.CoverageResultBind is not null||
            r.Stage is not(HappySunQuanStage.MovementChildren or HappySunQuanStage.RecoveryChildren))return false;
        RequireHappySunQuan(f);
        return r.Operation==SkillProgramEffectOp.ResolveHappySunQuanShehu?r.Payments.Count(p=>p.Index==r.Index&&p.CardId==r.ChosenCard&&p.Target==r.Target)==1:
            r.Operation==SkillProgramEffectOp.ResolveHappySunQuanDingli&&(r.Stage==HappySunQuanStage.RecoveryChildren&&r.Branch==1||r.Draw is{}d&&r.Branch==2&&d.RecipientSeat==f.OwnerSeat&&d.RequestedCount==r.Amount&&d.SequenceBefore==r.Before&&d.SequenceAfter==r.After&&d.ActualDrawIds.SequenceEqual(r.Paid.Select(m=>m.CardId))&&r.Paid.All(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==HappyDingliDraw));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,40940)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,40940)]
    private bool HappySunQuanRecovery(ProgramSkillFrame f,RecoveryReplacementFrame child)=>f.HappySunQuan is{Operation:SkillProgramEffectOp.ResolveHappySunQuanDingli,Stage:HappySunQuanStage.RecoveryChildren,Branch:1,Amount:1}&&
        RecoveryReplacementFrameRidesOn(child,f)&&child.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&child.Attempt.SourceSeat==f.OwnerSeat&&child.Attempt.TargetSeat==f.OwnerSeat&&child.Attempt.Amount==1&&
        child.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&child.Attempt.Completion.InstructionIndex==f.InstructionIndex;
    private bool HappySunQuanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.HappySunQuan is not{}r)return false;RequireHappySunQuan(f);
        if(child is RecoveryReplacementFrame recovery)return HappySunQuanRecovery(f,recovery);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is{}p&&HappySunQuanMovement(f,GetPausedPrivateOfferEffect(f),p)&&ProgramMovementReturnMatches(f,moved)&&
            moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.All(r.Paid.Contains);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&
            (r.Stage==HappySunQuanStage.RecoveryChildren&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.TargetSeat==f.OwnerSeat||
             r.Stage==HappySunQuanStage.HpLoss&&hp.Continuation is PostEventContinuation.Program or PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.TargetSeat==f.OwnerSeat);
        return child is DyingFrame dying&&r.Stage==HappySunQuanStage.HpLoss&&r.Branch==2&&dying.ParentFrameId==f.Id&&dying.VictimSeat==f.OwnerSeat&&dying.Continuation==DyingContinuationKind.ProgramSkill;
    }
    private ProgramSkillFrame? HappySunQuanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HappySunQuanFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,40940)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,40940)] private bool HappySunQuanDamageWindow(long id)=>HappySunQuanObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,40940)] private bool HappySunQuanBeforeDamage(long id)=>HappySunQuanObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,40940)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,40940)] private bool HappySunQuanDying()=>ActiveDying is{}d&&HappySunQuanObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,40940)] private bool AdvanceHappySunQuanChildren()=>_pendingDecision is null&&HappySunQuanObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,40940)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,40940)] private bool RetainHappySunQuanPaid(ProgramSkillFrame f)
    {
        if(f.HappySunQuan is{}r&&(r.Operation==SkillProgramEffectOp.ResolveHappySunQuanLearning||r.Operation==SkillProgramEffectOp.GrantHappySunQuanLearning&&r.Targets.Count>0||r.Payments.Count>0||r.Draw is not null))
        {RequireHappySunQuan(f);return true;}
        return f.SkillId==HappySunQuanComposition.Learning&&f.TriggerId=="resolve-study"&&f.InstructionIndex==0&&f.WindowContext is{}w&&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.BindingId==f.TriggerId&&e.SkillInstanceId==f.SkillInstanceId)==1&&
            IsIssuedHappyLearning(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority),w);
    }
}
