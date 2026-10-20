namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27080)]
    private void AssertRuanHui(ProgramSkillFrame f)
    {
        if(f.RuanHui is not{}r)return;
        if(r.Source!=RuanHuiSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||f.InstructionIndex!=1||r.ActualTurn!=_turnNumber||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!RuanHuiComposition.Handles(r.Op)||!Enum.IsDefined(r.Stage)||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||
            f.WindowContext is not{}context||context.ParentFrameId!=r.ParentId||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||
            ProgramEventHistory<RuanHuiStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||start.Source!=r.Source||start.GameplayHash!=r.GameplayHash||start.Op!=r.Op||
            start.ParentId!=r.ParentId||start.ActualTurn!=r.ActualTurn||start.InstructionIndex!=r.InstructionIndex||start.LeaseId!=r.LeaseId||start.ActionId!=r.ActionId)
            throw new InvalidOperationException("Ruan Hui lost her original source, native boundary or unique owning invoice.");
        var at=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(at<1)throw new InvalidOperationException("Ruan Hui lost her retained native parent.");
        if(r.Op==SkillProgramEffectOp.ObtainJingzhongUsedEntities)
        {
            if(context.Window!=SkillProgramTriggerWindow.DiscardPileReceived||context.MovementBatch is not{}batch||batch.Id!=r.ParentId||
                _resolutionStack[at-1]is not CardsMovedTriggerWindowFrame moved||moved.Id!=batch.Id||!moved.Batch.Movements.SequenceEqual(batch.Movements)||
                moved.CandidateIndex<0||moved.CandidateIndex>=moved.Candidates.Count||!MountObserverCandidateMatches(f,moved.Candidates[moved.CandidateIndex])||r.Stage!=RuanHuiStage.ClaimChildren||
                r.LeaseId is not{}lease||r.ActionId is not{}action||r.Entities.Count<1||r.Entities.Select(e=>e.CardId).Distinct().Count()!=r.Entities.Count||
                ProgramEventHistory<JingzhongClaimIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var claim]||claim.LeaseId!=lease||claim.ActionId!=action||claim.BatchId!=batch.Id||claim.EntityCount!=r.Entities.Count||
                ProgramEventHistory<JingzhongLeaseEvent>().SingleOrDefault(e=>e.FrameId==lease)is not{}grant||grant.Source.OwnerSeat!=f.OwnerSeat||grant.Source.SkillInstanceId!=f.SkillInstanceId||grant.GameplayHash!=f.GameplayHash||
                JingzhongDuePhase(grant)?.Token!=claim.PhaseToken||JingzhongUsedCount(lease)>3||r.Entities.Any(entity=>!ProgramEventHistory<JingzhongSettledEntityEvent>().Any(e=>
                    e.LeaseId==lease&&e.ActionId==action&&e.BatchId==batch.Id&&e.CardId==entity.CardId&&e.MovementSequence==entity.Sequence)))
                throw new InvalidOperationException("Jingzhong lost its exact phase lease, original Use entities or bounded whole-Use claim.");
        }
        else
        {
            if(_resolutionStack[at-1]is not ProgramLifecycleTriggerWindowFrame parent||parent.Id!=r.ParentId||parent.OwnerSeat!=f.OwnerSeat||parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||
                !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex])||parent.Window!=(r.Op==SkillProgramEffectOp.ResolveMingchaRevealDrawChoice?SkillProgramTriggerWindow.DrawPhaseStarting:SkillProgramTriggerWindow.DiscardPhaseEnded)||
                context.Window!=parent.Window||r.LeaseId is not null||r.ActionId is not null)
                throw new InvalidOperationException("Ruan Hui lost her actual draw or discard lifecycle cursor.");
            if(r.Op==SkillProgramEffectOp.ResolveMingchaRevealDrawChoice)
            {
                var revealed=ProgramEventHistory<RuanHuiRevealedEntityEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).ToArray();
                if(r.Entities.Count>3||r.Entities.Select(e=>e.CardId).Distinct().Count()!=r.Entities.Count||revealed.Length!=r.Entities.Count||
                    !revealed.Select(e=>e.Entity).SequenceEqual(r.Entities)||revealed.Where((e,i)=>e.Index!=i).Any()||r.Entities.Any(e=>
                        e.From!=CardLocation.Processing||!_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing)))
                    throw new InvalidOperationException("Mingcha cannot replace its original ordered public reveal with other physical entities.");
                var decisions=ProgramEventHistory<RuanHuiDecisionEvent>().Where(e=>e.FrameId==f.Id).ToArray();
                if(r.ReplaceDraw is null?decisions.Length!=0:decisions is not[var decision]||decision.ReplaceDraw!=r.ReplaceDraw||r.ReplaceDraw==true&&!parent.NormalDrawReplaced)
                    throw new InvalidOperationException("Mingcha changed its paid normal draw replacement decision.");
            }
        }
        var invoices=ProgramEventHistory<RuanHuiStepEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(invoices.Length!=r.Steps.Count||!invoices.Select(e=>e.Step).SequenceEqual(r.Steps)||r.Steps.GroupBy(s=>s.Stage).Any(g=>g.Count()!=1))
            throw new InvalidOperationException("Ruan Hui cannot pay or resume one native stage twice.");
        for(var i=0;i<r.Steps.Count;i++)
        {
            var step=r.Steps[i];var movements=_cardMovements.Where(m=>m.Sequence>step.Before&&m.Sequence<=step.After).ToArray();
            if(step.Index!=i||step.Before<0||step.After<step.Before||step.After>_movementSequence||step.Count<0||i>0&&step.Before<r.Steps[i-1].After)
                throw new InvalidOperationException("Ruan Hui lost her monotonic native payment range.");
            if(step.Stage==RuanHuiStage.RevealChildren)
            {if(step.Count!=r.Entities.Count||movements.Count(m=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing)!=step.Count)throw new InvalidOperationException("Mingcha reveal count differs from its actual native top-card movements.");}
            else if(step.Stage==RuanHuiStage.SettleChildren)
            {if(movements.Count(m=>m.Reason.Value is MingchaGain or MingchaCleanup)!=step.Count||movements.Where(m=>m.Reason.Value is MingchaGain or MingchaCleanup).Any(m=>
                r.Entities.SingleOrDefault(e=>e.CardId==m.CardId)is not{}entity||m.From!=CardLocation.Processing||m.To!=(r.ReplaceDraw==true&&entity.Rank<=8?CardLocation.Hand(f.OwnerSeat):CardLocation.DiscardPile)))throw new InvalidOperationException("Mingcha must settle only its actual reveal and frozen rank matching.");}
            else if(step.Stage==RuanHuiStage.RandomChildren)
            {if(r.RandomEntity is not{}entity||r.TargetSeat is not{}target||entity.From.OwnerSeat!=target||entity.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||step.Count!=1||
                movements.Count(m=>m.CardId==entity.CardId&&m.From==entity.From&&m.Reason.Value==MingchaRandom&&m.To==(entity.Kind==CardKind.GeneralWeapon?CardLocation.OutsideGame:CardLocation.Hand(f.OwnerSeat)))!=1||
                ProgramEventHistory<RuanHuiRandomEntityEvent>().Count(e=>e.FrameId==f.Id&&e.TargetSeat==target&&e.Entity==entity)!=1)throw new InvalidOperationException("Mingcha lost its one real seeded HE obtaining invoice.");}
            else if(step.Stage==RuanHuiStage.ClaimChildren)
            {if(step.Count!=r.Entities.Count||movements.Count(m=>m.Reason.Value==JingzhongGain&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&r.Entities.Any(e=>e.CardId==m.CardId))!=step.Count)throw new InvalidOperationException("Jingzhong cannot replay a gain or substitute an unavailable material.");}
            else throw new InvalidOperationException("Ruan Hui cannot record a payment for an unpaid choice.");
        }
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27080)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27080)]
    private bool ContinuePaidRuanHui(ProgramSkillFrame f)
    {
        if(f.RuanHui is not null){AssertRuanHui(f);return true;}
        return f.InstructionIndex is 0 or 1&&f.TriggerId is{}id&&f.ActivationId==id&&f.WindowContext is{}context&&
            IsIssuedRuanHuiCandidate(new(f.OwnerSeat,f.SkillId,id,f.SkillInstanceId,f.GameplayHash,0,context.OccurrenceIndex))&&
            CanRunIssuedRuanHuiCandidate(new(f.OwnerSeat,f.SkillId,id,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),context);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 260)]
    private bool IsRuanHuiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {if(f.RuanHui is not{}r||e?.Op!=r.Op||p!=f.PendingMovementContinuation||r.Stage is RuanHuiStage.DrawChoice or RuanHuiStage.TargetChoice)return false;AssertRuanHui(f);return true;}
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 790)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 730)]
    private bool IsRuanHuiRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(f.RuanHui is not{Stage:RuanHuiStage.RandomChildren,RandomEntity:{Kind:CardKind.SilverLion}card,Steps.Count:>0}r||f.PendingMovementContinuation is null)return false;
        AssertRuanHui(f);var step=r.Steps[^1];return RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&
            recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==card.From.OwnerSeat&&recovery.Attempt.TargetSeat==card.From.OwnerSeat&&
            _cardMovements.Any(m=>m.CardId==card.CardId&&m.Sequence>step.Before&&m.Sequence<=step.After&&m.From==card.From&&m.Reason.Value==MingchaRandom);
    }
    private bool RuanHuiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.RuanHui is not{}r)return false;AssertRuanHui(f);
        if(child is ProgramLifecycleTriggerWindowFrame skills&&skills.ResumeProgramFrameId==f.Id)return skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(f.PendingMovementContinuation is null||r.Steps.Count==0)return false;var step=r.Steps[^1];
        if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>step.Before&&m.Sequence<=step.After);
        if(child is RecoveryReplacementFrame recovery)return IsRuanHuiRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return r.RandomEntity is{Kind:CardKind.SilverLion}card&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1&&hp.Change.SourceSeat==card.From.OwnerSeat&&hp.Change.TargetSeat==card.From.OwnerSeat;
        return false;
    }
    private ProgramSkillFrame? RuanHuiObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.RuanHuiFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 620)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 730)]
    private bool HasRuanHuiDamageObserver(long id)=>RuanHuiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 760)]
    private bool HasRuanHuiBeforeDamageObserver(long id)=>RuanHuiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 580)]
    private bool IsRuanHuiDying()=>ActiveDying is{}d&&RuanHuiObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1220)]
    private bool AllowsRuanHuiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||RuanHuiObserverRoot()is not{}r||r.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27080)]
    private bool AdvanceRuanHuiSubtree()=>_pendingDecision is null&&RuanHuiObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
