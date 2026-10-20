namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27380)]
    private void AssertHuiyun(ProgramSkillFrame f)
    {
        if(f.Huiyun is not{}r)return;
        if(f.InstructionIndex!=1||f.SkillId!="ol:huiyun"||f.TriggerId!="completed-converted-fire-attack"||f.GameplayHash!=r.Origin.GameplayHash||f.SkillInstanceId!=r.Origin.Source.SkillInstanceId||f.OwnerSeat!=r.Origin.Source.OwnerSeat||f.WindowContext is not{}c||c.ParentFrameId!=r.ParentWindowId||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack||HuiyunCandidateUse(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),c)is not{}use||use.HuiyunOrigin!=r.Origin||r.Index<0||r.Index>r.Opportunities.Count||r.Option is <0 or >3||!Enum.IsDefined(r.Stage)||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.ActualCount<0||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||ProgramEventHistory<HuiyunStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||started!=new HuiyunStartedEvent(f.Id,r.Origin,r.ParentWindowId,r.Opportunities.Count))
            throw new InvalidOperationException("Huiyun lost its exact original completed FireAttack, binding, source/hash or owning opportunity cursor.");
        var issued=ProgramEventHistory<HuiyunOpportunityIssuedEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).ToArray();
        if(issued.Length!=r.Opportunities.Count||issued.Where((e,i)=>e.Index!=i||e.Opportunity!=r.Opportunities[i]).Any()||!r.Opportunities.SequenceEqual(HuiyunOpportunities(use))||Enumerable.Range(0,r.Index).Any(i=>ProgramEventHistory<HuiyunOpportunityCompletedEvent>().Count(e=>e.FrameId==f.Id&&e.Index==i)!=1)||ProgramEventHistory<HuiyunOpportunityCompletedEvent>().Any(e=>e.FrameId==f.Id&&e.Index>=r.Index))
            throw new InvalidOperationException("Huiyun changed or repeated an original actual effect/recipient/revealed-entity invoice.");
        if(r.Index==r.Opportunities.Count)return;
        var spent=ProgramEventHistory<HuiyunOptionSpentEvent>().Where(e=>e.FrameId==f.Id&&e.Index==r.Index).ToArray();
        if(r.Stage==HuiyunStage.OptionChoice?r.Option!=0||spent.Length!=0:spent is not[var option]||option!=new HuiyunOptionSpentEvent(f.Id,r.Index,r.Origin.Source,r.Origin.RoundNumber,r.Option)||ProgramEventHistory<HuiyunOptionSpentEvent>().Count(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId&&e.RoundNumber==r.Origin.RoundNumber&&e.Option==r.Option)!=1)
            throw new InvalidOperationException("Huiyun cannot pay the same round option twice or inherit a different option after target consent.");
        var movement=r.Stage is HuiyunStage.NativeChildren or HuiyunStage.RecastCostChildren or HuiyunStage.RecastDrawChildren or HuiyunStage.DrawChildren;
        if(movement?f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null}p||p.SubjectSeat!=HuiyunCurrent(r).TargetSeat:f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Huiyun lost its owning once-paid movement continuation.");
        if(r.Native is{}n)
        {
            if(r.Option is not(1 or 2)||n.ProgramFrameId!=f.Id||n.OpportunityIndex!=r.Index||n.Source!=r.Origin.Source||n.GameplayHash!=f.GameplayHash||n.ActorSeat!=HuiyunCurrent(r).TargetSeat||n.Plan.CardId!=n.CardId||n.Plan.ConversionSource is not null||(n.Plan.AdditionalConversionSources?.Count??0)!=0||r.Option==1&&n.CardId!=HuiyunCurrent(r).ShownCardId||r.Stage==HuiyunStage.Issuing&&(n.UseId!=0||n.ActionId!=0))
                throw new InvalidOperationException("Huiyun changed its natural Hand entity, consenting actor or preliminary native invoice.");
            if(r.Stage!=HuiyunStage.Issuing&&(n.UseId<=0||n.ActionId<=0||ProgramEventHistory<HuiyunNativeIssuedEvent>().Count(e=>SameHuiyunNative(e.Invoice,n))!=1||!HuiyunPaymentProof(n.UseId,n.ActionId,n.CardId,CardLocation.Hand(n.ActorSeat),CardLocation.Processing,HuiyunNativePaymentReason(n),n.BeforePayment,true)))
                throw new InvalidOperationException("Huiyun lost one real natural-card Hand payment and exact issued native identity.");
            if(r.Stage==HuiyunStage.NativeUse&&!r.NativeCompleted&&(LifecycleCardUse(n.UseId)is not{}active||!HuiyunNativeUseRidesOn(active,f)))throw new InvalidOperationException("Huiyun native use must remain its true owning child until completion.");
            if(r.Stage is HuiyunStage.NativeChildren or HuiyunStage.RecastCostChildren or HuiyunStage.RecastDrawChildren&&(ProgramEventHistory<HuiyunNativeReturnedEvent>().Count(e=>SameHuiyunNative(e.Invoice,n))!=1||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==n.UseId)!=1||LifecycleCardUse(n.UseId)is not null||!r.NativeCompleted))throw new InvalidOperationException("Huiyun cannot recast before its whole native use and attack have returned.");
        }
        else if(r.Stage is HuiyunStage.Issuing or HuiyunStage.NativeUse or HuiyunStage.NativeChildren or HuiyunStage.RecastCostChildren or HuiyunStage.RecastDrawChildren||r.NativeCompleted)throw new InvalidOperationException("Huiyun cannot recast without an actual accepted natural-card use.");
        if(r.Stage is HuiyunStage.RecastCostChildren or HuiyunStage.RecastDrawChildren or HuiyunStage.DrawChildren)
        {
            var paid=ProgramEventHistory<HuiyunMovementPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Index==r.Index&&e.Stage==r.Stage).ToArray();
            if(paid is not[var m]||m!=new HuiyunMovementPaidEvent(f.Id,r.Index,r.Stage,r.Before,r.After,r.ActualCount))throw new InvalidOperationException("Huiyun lost its paid original movement range.");
            if(r.Stage==HuiyunStage.RecastCostChildren)
            {
                var recasts=ProgramEventHistory<HuiyunRecastCardEvent>().Where(e=>e.FrameId==f.Id&&e.Index==r.Index).ToArray();
                if(recasts.Length!=r.ActualCount||recasts.Select(e=>e.CardId).Distinct().Count()!=recasts.Length||recasts.Any(e=>e.HolderSeat!=HuiyunCurrent(r).TargetSeat||!_cardMovements.Any(x=>x.Sequence==e.Sequence&&x.Sequence>r.Before&&x.Sequence<=r.After&&x.CardId==e.CardId&&x.CardKind==e.Kind&&x.From==CardLocation.Hand(e.HolderSeat)&&x.To==(GetAdvancedCard(e.CardId).IsGeneralWeapon?CardLocation.OutsideGame:CardLocation.DiscardPile)&&x.Reason==CardMoveReasons.RecastDiscard)))throw new InvalidOperationException("Huiyun recasts only its real remaining Hand or surviving original shown entity once.");
            }
            else if(_cardMovements.Count(x=>x.Sequence>r.Before&&x.Sequence<=r.After&&x.From==CardLocation.DrawPile&&x.To==CardLocation.Hand(HuiyunCurrent(r).TargetSeat)&&x.Reason==(r.Stage==HuiyunStage.RecastDrawChildren?CardMoveReasons.RecastDraw:new CardMoveReason("skill-program.hu-ban.huiyun.draw")))!=r.ActualCount)throw new InvalidOperationException("Huiyun draw reward cannot count a shuffle or another child's gain.");
        }
        if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is{}prompt&&r.Stage is HuiyunStage.OptionChoice or HuiyunStage.HandUseChoice&&(prompt.PlayerSeat!=(r.Stage==HuiyunStage.OptionChoice?f.OwnerSeat:HuiyunCurrent(r).TargetSeat)||!AssistedChoicesEqual(prompt.Choices,HuiyunChoices(f))))throw new InvalidOperationException("Huiyun lost its exact currently published private target choice.");
    }
    private static CardMoveReason HuiyunNativePaymentReason(HuiyunNativeInvoice n)=>n.Plan.Kind==LegalActionKind.Equip?CardMoveReasons.EquipmentUse:n.Plan.Kind==LegalActionKind.IronChain?CardMoveReasons.IronChainUse:CardMoveReasons.Use;
    private bool HasHuiyunCompletedUseObserver(long useId)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==useId);
        if(index<0||index+2>=_resolutionStack.Count||_resolutionStack[index]is not CardUseFrame{Step:ResolutionFrameStep.Completed,HuiyunOrigin:{}origin}use||_resolutionStack[index+1]is not ProgramCardTriggerWindowFrame{Continuation:ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard}window||window.ParentFrameId!=use.Id||_resolutionStack[index+2]is not ProgramSkillFrame{Huiyun:{}r}f||r.Origin!=origin||r.ParentWindowId!=window.Id||f.WindowContext is not{}context||HuiyunCandidateUse(new(f.OwnerSeat,f.SkillId,f.TriggerId??"",f.SkillInstanceId,f.GameplayHash,0),context)?.Id!=useId||ProgramEventHistory<HuiyunStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||started.Origin!=origin||started.ParentWindowId!=window.Id||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==useId)!=1||!Enum.IsDefined(r.Stage)||r.Index<0||r.Index>r.Opportunities.Count)return false;
        return index+3==_resolutionStack.Count||HuBanFirstChild(f,_resolutionStack[index+3]);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 330)]
    private bool IsHuBanMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.Huiyun is{Stage:HuiyunStage.NativeChildren or HuiyunStage.RecastCostChildren or HuiyunStage.RecastDrawChildren or HuiyunStage.DrawChildren}r&&e?.Op==SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack&&p==f.PendingMovementContinuation&&p.SubjectSeat==HuiyunCurrent(r).TargetSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool IsHuiyunNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)
    {
        if(f.Huiyun is not{Stage:HuiyunStage.NativeChildren,NativeCompleted:true,Native:{}n}r||f.PendingMovementContinuation is not{}pending||!IsHuBanMovement(f,GetPausedPrivateOfferEffect(f),pending)||n.ProgramFrameId!=f.Id||n.OpportunityIndex!=r.Index||r.Before!=n.BeforePayment||LifecycleCardUse(n.UseId)is not null||ProgramEventHistory<HuiyunNativeReturnedEvent>().Where(e=>e.Invoice.UseId==n.UseId).ToArray()is not[var returned]||!SameHuiyunNative(returned.Invoice,n)||returned.After!=r.After||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==n.UseId)!=1||batch.ParentFrameId!=n.UseId||batch.AwaitingProgramFrameId is not null||batch.OriginOwnerSeat!=f.OwnerSeat||batch.OriginSkillId!=f.SkillId||batch.OriginSkillInstanceId!=f.SkillInstanceId||batch.Movements.Count==0)return false;
        return batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
    }
    private bool HuBanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.Huiyun is not{}r)return false;AssertHuiyun(f);
        if(child is CardUseFrame use)return HuiyunNativeUseRidesOn(use,f);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}changed)return changed.ResumeProgramFrameId==f.Id&&changed.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is not CardsMovedTriggerWindowFrame moved||f.PendingMovementContinuation is null)return false;
        if(r.Stage==HuiyunStage.NativeChildren)return moved.ResumeProgramFrameId==f.Id&&IsHuiyunNativeSettlementBatch(f,moved.Batch);
        if(!ProgramMovementReturnMatches(f,moved))return false;
        if(r.Stage==HuiyunStage.RecastCostChildren)return moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&ProgramEventHistory<HuiyunRecastCardEvent>().Any(p=>p.FrameId==f.Id&&p.Index==r.Index&&p.CardId==m.CardId&&p.Sequence==m.Sequence));
        return r.Stage is HuiyunStage.RecastDrawChildren or HuiyunStage.DrawChildren&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&(m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(HuiyunCurrent(r).TargetSeat)&&m.Reason==(r.Stage==HuiyunStage.RecastDrawChildren?CardMoveReasons.RecastDraw:new CardMoveReason("skill-program.hu-ban.huiyun.draw"))));
    }
    private ProgramSkillFrame? HuBanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HuBanFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private int? HuBanAnyObserverRootIndex()
    {
        if(HuBanObserverRoot()is{}program)return _resolutionStack.FindIndex(f=>f.Id==program.Id);
        for(var i=0;i+1<_resolutionStack.Count;i++)
        {
            if(_resolutionStack[i]is not CardUseFrame{RecoveryPaidContinuation:{}continuation}use||!HasHuiyunTrickPaymentTail(use,continuation))continue;
            var first=_resolutionStack[i+1];
            var native=first is CardsMovedTriggerWindowFrame moved&&IsPaidCardUseMovementReturn(use,moved)||first is HpChangedTriggerWindowFrame hp&&hp.Change.ParentFrameId==use.Id&&hp.ResumeFrameId==use.Id&&hp.Continuation==PostEventContinuation.RecoveryPaidCardUse||RecoveryReplacementFrameRidesOn(first,use);
            if(native&&HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)))return i;
        }
        return null;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 840)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 950)]
    private bool HasHuBanDamageObserver(long id)=>HuBanAnyObserverRootIndex()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>root;
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 980)]
    private bool HasHuBanBeforeDamageObserver(long id)=>HuBanAnyObserverRootIndex()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>root;
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 810)]
    private bool IsHuBanDying()=>ActiveDying is{}d&&HuBanAnyObserverRootIndex()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>root;
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1380)]
    private bool AllowsHuBanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||HuBanAnyObserverRootIndex()is not{}root||_resolutionStack.FindIndex(p=>p.Id==f.Id)<=root)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27380)]private bool AdvanceHuBanSubtree()=>_pendingDecision is null&&HuBanAnyObserverRootIndex()is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27381)]private bool ContinueIssuedHuiyun(ProgramSkillFrame f)=>f.Huiyun is null&&f.InstructionIndex is 0 or 1&&f.TriggerId=="completed-converted-fire-attack"&&f.WindowContext is{}c&&CanRunIssuedHuiyunCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),c);
}
