namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,41200)] private void AssertHappyLuBu(ProgramSkillFrame f)
    {
        if(f.HappyLuBu is not{}r)return;
        if(r.Source!=HappyLuBuSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||r.ActualTurn!=_turnNumber||r.ParentId!=f.WindowContext?.ParentFrameId||
            !HappyLuBuComposition.Owns(r.Operation)||r.Before<0||r.After<r.Before||r.After>_movementSequence||!Enum.IsDefined(r.Stage)||
            ProgramEventHistory<HappyLuBuStartedEvent>().Count(e=>e==new HappyLuBuStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentId,r.ActualTurn,r.UseId,r.ActionId))!=1)
            throw new InvalidOperationException("Happy Lu Bu lost his original instruction, exact source, turn or receipt.");
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(index<1||_resolutionStack[index-1].Id!=r.ParentId)throw new InvalidOperationException("Happy Lu Bu lost his owning native parent.");
        if(r.Operation==SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic)
        {
            if(_resolutionStack[index-1] is not ProgramLifecycleTriggerWindowFrame p||!IsHappyLuBuActualStart(p)||p.CandidateIndex!=0||!MountObserverCandidateMatches(f,p.Candidates[0])||r.UseId!=0||r.ActionId!=0||r.Paid.Count>1||
                r.Paid.Any(m=>m.From!=CardLocation.DrawPile||m.To!=CardLocation.Hand(f.OwnerSeat)||m.Reason.Value!=HappyLuBuBasicReason||m.Sequence<=r.Before||m.Sequence>r.After||
                    GetProgramCardCategory(FindCardById(m.CardId).Kind)!=SkillProgramCardCategory.Basic||_cardMovements.Count(a=>a==m)!=1)||
                r.Paid.Count>0&&ProgramEventHistory<HappyLuBuBasicPaidEvent>().Count(e=>e==new HappyLuBuBasicPaidEvent(f.Id,r.Before,r.After,r.Paid.Count))!=1)
                throw new InvalidOperationException("Wuqian lost the actual-turn owned gain and real basic entity payment.");
        }
        else if(_resolutionStack[index-1] is not ProgramCardTriggerWindowFrame p||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,ToSharedCandidate(p.Candidates[p.CandidateIndex]))||
            LifecycleCardUse(r.UseId) is not{HappyLuBuWuqian:{}policy}use||policy.ProgramId!=f.Id||policy.ActionId!=r.ActionId||policy.Source!=r.Source||p.Action.ActionId!=r.ActionId||use.SourceSeat!=f.OwnerSeat||!HappyLuBuFirstUse(use)||r.Paid.Count!=0)
            throw new InvalidOperationException("Wuqian lost its exact first real Use and target-window candidate.");
    }
    private Card FindCardById(int id)=>_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,41200)] private bool HappyLuBuMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)
    {
        if(effect?.Op!=SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic||f.HappyLuBu is not{Stage:HappyLuBuStage.BasicChildren,Paid.Count:1}r||pending!=f.PendingMovementContinuation||pending.SubjectSeat!=f.OwnerSeat||pending.BeforeCount!=0||pending.CoverageResultBind is not null)return false;
        AssertHappyLuBu(f);return r.After>r.Before;
    }
    private bool HappyLuBuFirstChild(ProgramSkillFrame f,ResolutionFrame child)=>f.HappyLuBu is{Stage:HappyLuBuStage.BasicChildren}r&&f.PendingMovementContinuation is{}pending&&HappyLuBuMovement(f,GetPausedPrivateOfferEffect(f),pending)&&
        child is CardsMovedTriggerWindowFrame moved&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.All(r.Paid.Contains);
    private ProgramSkillFrame? HappyLuBuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HappyLuBuFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,41200)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,41200)]
    private bool HappyLuBuDamage(long id)=>HappyLuBuObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,41200)] private bool HappyLuBuBeforeDamage(long id)=>HappyLuBuObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,41200)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime,41200)]
    private bool HappyLuBuDying()=>ActiveDying is{}d&&HappyLuBuObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,41200)] private bool AdvanceHappyLuBuObserver()=>_pendingDecision is null&&HappyLuBuObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,41200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,41200)]
    private bool RetainHappyLuBuPaid(ProgramSkillFrame f)
    {
        if(f.HappyLuBu is{Paid.Count:>0}){AssertHappyLuBu(f);return true;}
        return f.SkillId==HappyLuBuComposition.Shenfen&&f.TriggerId is null&&f.ActivationId=="limited-destruction"&&f.InstructionIndex>=1&&
            ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id&&e.SkillId==f.SkillId&&e.TargetSeat==f.OwnerSeat&&e.Amount==3)==1&&
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.ActivationId==f.ActivationId)==1;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish,41200)] private void AssertHappyLuBuUses()
    {
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.HappyLuBuWuqian is not null))
        {
            var r=use.HappyLuBuWuqian!;
            if(r.UseId!=use.Id||r.ActionId!=use.Action?.ActionId||r.Source.OwnerSeat!=use.SourceSeat||r.Source.SkillId!=HappyLuBuComposition.Wuqian||r.Source.BindingId!="first-kind-use"||
                r.GameplayHash!=_contentRegistry.GetSkill(r.Source.SkillId).Program!.GameplayHash||r.ActualTurn!=_turnNumber||r.OriginalTargets.Count<1||r.OriginalTargets.Distinct().Count()!=r.OriginalTargets.Count||
                r.AddedTarget is{}extra&&(r.OriginalTargets.Contains(extra)||ProgramEventHistory<CurrentCardEnhancedEvent>().Count(e=>e.CardUseFrameId==use.Id&&e.CardActionId==r.ActionId&&e.OwnerSeat==r.Source.OwnerSeat&&e.Enhancement==CurrentCardEnhancement.ExtraTarget&&e.ExtraTargetSeat==extra)!=1)||
                ProgramEventHistory<HappyLuBuStartedEvent>().Count(e=>e==new HappyLuBuStartedEvent(r.ProgramId,r.Source,r.GameplayHash,SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse,r.WindowId,r.ActualTurn,use.Id,r.ActionId))!=1||
                r.Applied&&(ProgramEventHistory<HappyLuBuUseAppliedEvent>().Count(e=>e==new HappyLuBuUseAppliedEvent(r.ProgramId,use.Id,r.ActionId,r.AddedTarget))!=1||!use.IgnoresArmor||(use.Enhancements&CurrentCardEnhancement.IgnoreArmor)==0))
                throw new InvalidOperationException("Wuqian lost the frozen issued real-use policy and exact source proof.");
        }
    }
    private bool IsHappyLuBuTrueVirtualProducer(CardUseFrame use,ProgramSkillFrame parent,SkillProgramEffect paused)
    {
        if(paused is not{Op:SkillProgramEffectOp.UseVirtualCard,OutputKind:CardKind.Slash,UseCardActionWindows:true}||
            use.HappyLuBuWuqian is not{Applied:true,Kind:CardKind.Slash,OriginalTargets:[var original],AddedTarget:{}extra}r||use.Action is not{}action||
            r.UseId!=use.Id||r.ActionId!=action.ActionId||r.ActualTurn!=_turnNumber||r.Source.OwnerSeat!=use.SourceSeat||r.Source.SkillId!=HappyLuBuComposition.Wuqian||r.Source.BindingId!="first-kind-use"||
            r.GameplayHash!=_contentRegistry.GetSkill(r.Source.SkillId).Program!.GameplayHash||r.ProgramId==parent.Id||parent.OwnerSeat!=use.SourceSeat||parent.SelectedTargetSeats is not[var selected]||selected!=original||original==extra||
            !action.ConversionChain.SequenceEqual([new CardConversionSource(parent.SkillId,GetProgramBindingId(parent),parent.OwnerSeat,parent.SkillInstanceId)])||!HappyLuBuFirstUse(use))return false;
        return ProgramEventHistory<HappyLuBuStartedEvent>().Count(e=>e==new HappyLuBuStartedEvent(r.ProgramId,r.Source,r.GameplayHash,SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse,r.WindowId,r.ActualTurn,use.Id,r.ActionId))==1&&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==r.ProgramId&&e.OwnerSeat==r.Source.OwnerSeat&&e.SkillId==r.Source.SkillId&&e.BindingId==r.Source.BindingId&&e.SkillInstanceId==r.Source.SkillInstanceId&&e.Window==SkillProgramTriggerWindow.CardUseCommitted)==1&&
            ProgramEventHistory<HappyLuBuUseAppliedEvent>().Count(e=>e==new HappyLuBuUseAppliedEvent(r.ProgramId,use.Id,r.ActionId,extra))==1&&
            ProgramEventHistory<CurrentCardEnhancedEvent>().Count(e=>e.CardUseFrameId==use.Id&&e.CardActionId==r.ActionId&&e.OwnerSeat==r.Source.OwnerSeat&&e.Enhancement==CurrentCardEnhancement.ExtraTarget&&e.ExtraTargetSeat==extra)==1&&
            ProgramEventHistory<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==use.Id&&e.CardId==0&&e.CardKind==CardKind.Slash&&e.SourceSeat==use.SourceSeat)==1&&
            ProgramEventHistory<TargetsConfirmedEvent>().Count(e=>e.ResolutionId==use.Id&&e.TargetSeats.SequenceEqual([original]))==1;
    }
}
