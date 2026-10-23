namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,40750)] private void RequireAnnZl(ProgramSkillFrame f)
    {
        if (f.AnniversaryZhangLiao is not { } r) return;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if (f.InstructionIndex!=1||r.Source!=AnnZlSource(f)||r.GameplayHash!=f.GameplayHash||r.ParentId!=f.WindowContext?.ParentFrameId||
            !AnniversaryZhangLiaoComposition.Owns(r.Operation)||!Enum.IsDefined(r.Stage)||r.ActualTurn!=_turnNumber||!IsValidPlayerSeat(r.Target)||
            r.Count<1||r.Count>5||r.PaidCount<0||r.PaidCount>r.Count||r.Before<0||r.After<r.Before||r.After>_movementSequence||
            index<1||_resolutionStack[index-1].Id!=r.ParentId||
            ProgramEventHistory<AnniversaryZhangLiaoStartedEvent>().Count(e=>e==new AnniversaryZhangLiaoStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentId,r.ActualTurn,r.Target,r.Count))!=1)
            throw new InvalidOperationException("Anniversary Zhang Liao lost his original parent, instance, execution cursor or scalar invoice.");
        if (r.Operation==SkillProgramEffectOp.ResolveAnniversaryZhangLiaoCuixi && !IsForeignActualTurnPindian(f))
            throw new InvalidOperationException("Cuixi lost the frozen actual turn candidate.");
        if (r.Operation==SkillProgramEffectOp.ResolveAnniversaryZhangLiaoDuorui &&
            (_resolutionStack[index-1] is not BeforeDamageProgramWindowFrame parent||parent.SourceSeat!=f.OwnerSeat||parent.TargetSeat!=r.Target||parent.Amount<=0||
             parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||!MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].Candidate)))
            throw new InvalidOperationException("Duorui lost its exact source-damage candidate.");
        if (r.Paid.Any(m=>m.Sequence<=r.Before||m.Sequence>r.After||_cardMovements.Count(actual=>actual==m)!=1))
            throw new InvalidOperationException("Zhang Liao lost a frozen actual payment movement.");
        if(r.Payments.Count!=r.PaidCount||r.Payments.Where((p,i)=>p.Ordinal!=i+1||p.Before<0||p.After<p.Before||p.After>_movementSequence||
            p.Movements.Any(m=>m.Sequence<=p.Before||m.Sequence>p.After||_cardMovements.Count(actual=>actual==m)!=1)||
            ProgramEventHistory<AnniversaryZhangLiaoPaidEvent>().Count(e=>e==new AnniversaryZhangLiaoPaidEvent(f.Id,p.Ordinal,p.Before,p.After,p.Movements.Count))!=1).Any())
            throw new InvalidOperationException("Zhang Liao lost an earlier completed cost's original movement receipt.");
        if (r.Stage is AnniversaryZhangLiaoStage.PaymentChildren or AnniversaryZhangLiaoStage.DiscardChildren or AnniversaryZhangLiaoStage.SlotChildren &&
            ProgramEventHistory<AnniversaryZhangLiaoPaidEvent>().Count(e=>e==new AnniversaryZhangLiaoPaidEvent(f.Id,r.PaidCount,r.Before,r.After,r.Paid.Count))!=1)
            throw new InvalidOperationException("Zhang Liao lost the unique completed payment invoice.");
        if (r.Slot is { } slot && (r.CapacityBefore<=0||ProgramEventHistory<AnniversaryDuoruiLeaseEvent>().Count(e=>e==new AnniversaryDuoruiLeaseEvent(f.Id,r.Source,r.Target,slot,r.CapacityBefore,r.PhaseToken))!=1))
            throw new InvalidOperationException("Duorui lost the issued temporary capacity lease.");
        if (r.Branch>0&&ProgramEventHistory<AnniversaryCuixiBranchEvent>().Count(e=>e==new AnniversaryCuixiBranchEvent(f.Id,r.Source,r.ActualTurn,r.Target,r.Branch))!=1)
            throw new InvalidOperationException("Cuixi lost the other player's paid branch.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,40750)]
    private bool AnnZlMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)
    {
        if (f.AnniversaryZhangLiao is not { } r||effect?.Op!=r.Operation||pending!=f.PendingMovementContinuation||pending.SubjectSeat!=f.OwnerSeat||pending.BeforeCount!=0||pending.CoverageResultBind is not null||
            r.Stage is not (AnniversaryZhangLiaoStage.PaymentChildren or AnniversaryZhangLiaoStage.DiscardChildren or AnniversaryZhangLiaoStage.SlotChildren or AnniversaryZhangLiaoStage.DrawChildren)) return false;
        RequireAnnZl(f);
        return r.Stage==AnniversaryZhangLiaoStage.DrawChildren ? r.Draw is { RecipientSeat:var recipient,RequestedCount:2 }&&recipient==f.OwnerSeat&&r.Branch==1 : r.PaidCount>0;
    }
    private bool AnnZlFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if (f.AnniversaryZhangLiao is not { } r) return false; RequireAnnZl(f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
                f.PendingMovementContinuation is { } p&&AnnZlMovement(f,GetPausedPrivateOfferEffect(f),p)&&ProgramMovementReturnMatches(f,moved)&&
                moved.Batch.Movements.All(m=>r.Paid.Contains(m));
        if (child is RecoveryReplacementFrame recovery)
            return f.PendingMovementContinuation is not null&&r.Paid.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==recovery.Attempt.TargetSeat)&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion;
        if (child is HpChangedTriggerWindowFrame hp)
            return f.PendingMovementContinuation is not null&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&
                hp.Change.Kind==HpChangeKind.Recovery&&r.Paid.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==hp.Change.TargetSeat);
        return r.Stage==AnniversaryZhangLiaoStage.Damage&&f.AttackAttempt is {SourceLess:false,Nature:DamageNature.Normal} a&&a.SourceSeat==f.OwnerSeat&&f.AttackReturn is not null&&DyingSuitsStructuralEdge(f,child);
    }
    private ProgramSkillFrame? AnnZlObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.AnnZlFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,40750)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,40750)] private bool AnnZlDamageObserver(long id)=>AnnZlObserverRoot() is { } root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,40750)] private bool AnnZlBeforeDamageObserver(long id)=>AnnZlObserverRoot() is { } root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,40750)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,40750)] private bool AnnZlDying()=>ActiveDying is { } dying&&AnnZlObserverRoot() is { } root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,40750)] private bool AdvanceAnnZlChildren()=>_pendingDecision is null&&AnnZlObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,40750)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,40750)] private bool RetainAnnZlPaid(ProgramSkillFrame f)
    { if(f.AnniversaryZhangLiao is not { PaidCount:>0 })return false;RequireAnnZl(f);return true; }
}
