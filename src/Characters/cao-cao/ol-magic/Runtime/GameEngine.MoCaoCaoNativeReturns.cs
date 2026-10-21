namespace CardGame.Core;

// Adapter proofs describe native adjacency and paid movement only. Choices,
// cursors, simultaneous commitments and all state progression belong to Runtime.
public sealed partial class GameEngine
{
    private bool MoCaoCaoPaidMovement(ProgramSkillFrame f,out int card,out int payer,out long before,out long after,out string reason)
    {
        card=payer=0;before=after=0;reason="";
        if (!MoCaoCaoBachao.Valid(f)) return false;
        if (f.MoCaoCaoBaibi is { Paid:true } b)
        {card=b.CardId;payer=b.Claim.TargetSeat;before=b.Before;after=b.After;reason=MoCaoCaoBachaoRuntime.BaibiReason;}
        else if(f.MoCaoCaoBachao is { Stage:MoCaoCaoBachaoStage.PaymentChildren } r && r.Cursor<r.Votes.Count)
        {card=r.PaidCardId;payer=r.Votes[r.Cursor].Seat;before=r.Before;after=r.After;reason=MoCaoCaoBachaoRuntime.GiftReason;}
        else return false;
        var id=card;var seat=payer;var lower=before;var upper=after;var why=reason;
        return id>0 && lower>=0 && upper<=_movementSequence && _cardMovements.Any(m=>m.CardId==id && m.From.OwnerSeat==seat &&
            m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.Sequence>lower && m.Sequence<=upper && m.Reason.Value==why &&
            (m.To==CardLocation.Hand(f.OwnerSeat) || m.To==CardLocation.OutsideGame));
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,33800)]
    private bool MoCaoCaoMovementReturn(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p) =>
        effect?.Op is SkillProgramEffectOp.ResolveMoCaoCaoBachao or SkillProgramEffectOp.ResolveMoCaoCaoBaibi &&
        MoCaoCaoBachao.Valid(f) && p==f.PendingMovementContinuation && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    private bool MoCaoCaoFirstNativeChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if (!MoCaoCaoBachao.Valid(f)) return false;
        if (MoCaoCaoPaidMovement(f,out var card,out var payer,out var before,out var after,out var reason))
        {
            if (child is CardsMovedTriggerWindowFrame moved)
                return ProgramMovementReturnMatches(f,moved) && moved.Batch.ParentFrameId==f.Id && moved.Batch.OriginOwnerSeat==f.OwnerSeat &&
                    moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.Count>0 &&
                    moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>before && m.Sequence<=after);
            var lion=_cardMovements.Any(m=>m.CardId==card && m.CardKind==CardKind.SilverLion && m.From==CardLocation.Equipment(payer) && m.Sequence>before && m.Sequence<=after);
            if (lion && child is RecoveryReplacementFrame recovery)
                return RecoveryReplacementFrameRidesOn(recovery,f) && recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement &&
                    recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value==reason &&
                    recovery.Attempt.SourceSeat==payer && recovery.Attempt.TargetSeat==payer && recovery.Attempt.Amount==1;
            if (lion && child is HpChangedTriggerWindowFrame hp)
                return hp.Change.ParentFrameId==f.Id && hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.AwaitedProgramMovement &&
                    hp.Change.Kind==HpChangeKind.Recovery && hp.Change.SourceSeat==payer && hp.Change.TargetSeat==payer &&
                    ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e=>e.ResolutionId>f.Id && e.ResolutionId<hp.Change.Id && e.PlayerSeat==payer && e.Reason.Value==reason && e.RecoveredAmount==hp.Change.Amount && e.RemainingHp==hp.Change.HpAfter);
        }
        if (f.MoCaoCaoBachao is { Stage:MoCaoCaoBachaoStage.Damage,TargetSeat:{ } target } &&
            f.AttackAttempt is { } attack && f.AttackReturn is not null && attack.SourceSeat==f.OwnerSeat && attack.TargetSeat==target && !attack.SourceLess)
            return DyingSuitsStructuralEdge(f,child);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId==f.Id &&
            (state.Window==SkillProgramTriggerWindow.SkillsChanged && state.Continuation==ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id && e.Change.ParentFrameId==f.Id));
    }
    private bool MoCaoCaoNativeEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i-1],_resolutionStack[i]) || _resolutionStack[i-1] is ProgramSkillFrame f && MoCaoCaoFirstNativeChild(f,_resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i-1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i-1]);
    private bool MoCaoCaoNativeSuffix(int n) => _resolutionStack[n+1] is DyingFrame d && n+2<_resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n+1,d) ||
        HasObserverSuffix(n,static(g,i)=>g.MoCaoCaoNativeEdge(i),static(g,i,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MoCaoCaoNativeRoot() => FindProgramObserverRoot(static(g,f,c)=>g.MoCaoCaoFirstNativeChild(f,c),static(g,n)=>g.MoCaoCaoNativeSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,33800)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,33800)]
    private bool MoCaoCaoDamageWindow(long id) => MoCaoCaoNativeRoot() is { } f && _resolutionStack.FindIndex(x=>x.Id==id && x is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,33800)]
    private bool MoCaoCaoBeforeDamageWindow(long id) => MoCaoCaoNativeRoot() is { } f && _resolutionStack.FindIndex(x=>x.Id==id && x is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,33800)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,33800)]
    private bool MoCaoCaoDying() => ActiveDying is { } d && MoCaoCaoNativeRoot() is { } f && _resolutionStack.FindIndex(x=>x.Id==d.Id)>_resolutionStack.FindIndex(x=>x.Id==f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,33800)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,33800)]
    private bool MoCaoCaoNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id!=f.Id || sourceLess) return false;
        if (MoCaoCaoBachao.Valid(f) && f.MoCaoCaoBachao is { Stage:MoCaoCaoBachaoStage.Damage,TargetSeat:{ } selected })
            return target==selected && amount==1 && source is null && nature is null;
        if (MoCaoCaoNativeRoot() is not { } root || root.Id==f.Id) return false;
        var e=GetPausedPrivateOfferEffect(f);
        return e is {Op:SkillProgramEffectOp.Damage} && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature &&
            target==(e.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,33800)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,33800)]
    private bool MoCaoCaoRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(MoCaoCaoFirstNativeChild(f,recovery)) return true;
        var n=_resolutionStack.FindIndex(x=>x.Id==f.Id);var c=_resolutionStack.FindIndex(x=>x.Id==recovery.Id);
        return n>=0 && c>n+1 && MoCaoCaoFirstNativeChild(f,_resolutionStack[n+1]) && MoCaoCaoNativeSuffix(n) && RecoveryReplacementFrameRidesOn(recovery,_resolutionStack[c-1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,33800)]
    private bool MoCaoCaoAdvanceNativeChildren() => _pendingDecision is null && MoCaoCaoNativeRoot() is not null && TryAdvanceObserverSubtree(true);
}
