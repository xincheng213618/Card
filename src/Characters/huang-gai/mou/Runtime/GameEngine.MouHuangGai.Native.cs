namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<int> MouHgTargets(ProgramSkillFrame f)
    {
        if(f.MouHuangGai is not{Reveals.Count:>0}r||!_players[f.OwnerSeat].IsAlive)return [];
        var shown=r.Reveals[^1];if(!IsSlashCard(shown.Kind)||_cardZones.GetLocation(shown.CardId)!=CardLocation.Processing)return [];
        var card=GetAdvancedCard(shown.CardId);var actor=_players[f.OwnerSeat];
        return Array.AsReadOnly(_players.Where(t=>CanUseSlashTarget(actor,t,card,effectiveKind:shown.Kind)).Select(t=>t.Seat).ToArray());
    }
    private void DiscardMouHgSlash(ProgramSkillFrame f)
    {
        var shown=f.MouHuangGai!.Reveals[^1];ReplaceRuntimeTop(f=f with{MouHuangGai=f.MouHuangGai with{Stage=MouHgStage.DiscardSlashChildren,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(_cardZones.GetLocation(shown.CardId)==CardLocation.Processing)MoveCard(GetAdvancedCard(shown.CardId),CardLocation.Processing,CardLocation.DiscardPile,new(MouHgDiscardReason),m=>SealMouHgMoves(f.Id,[m]));
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{MouHuangGai=f.MouHuangGai! with{After=_movementSequence}});ContinueMouHg(f.Id);
    }
    private void IssueMouHgSlash(ProgramSkillFrame f,int targetSeat)
    {
        if(!ValidMouHg(f)||f.MouHuangGai is not{Stage:MouHgStage.Target}r||!MouHgTargets(f).Contains(targetSeat))throw new InvalidOperationException("Quzhou requires its actual revealed Slash and current legal target.");
        var shown=r.Reveals[^1];var card=GetAdvancedCard(shown.CardId);var actor=_players[f.OwnerSeat];var target=_players[targetSeat];
        ReplaceRuntimeTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.NativeUse,Before=_movementSequence,After=_movementSequence}});
        var id=BeginCardUse(card,actor.Seat,[targetSeat],playedCardKind:shown.Kind,physicalCardIds:[card.Id],ignoresArmor:HasCardArmorBypass(actor,target,shown.Kind));var use=LifecycleCardUse(id)!;
        if(use.Action is not{Type:CardActionType.Use}a||a.PhysicalCards is not[{From:var from,CardId:var material}]||from!=CardLocation.Processing||material!=card.Id)throw new InvalidOperationException("Quzhou must use its real Processing entity.");
        var at=_resolutionStack.FindIndex(x=>x.Id==id);
        if(at<1||_resolutionStack[at-1]is not ProgramSkillFrame{MouHuangGai:{Stage:MouHgStage.NativeUse,Native:null}paid}parent||parent.Id!=f.Id||paid.Source!=r.Source||paid.GameplayHash!=r.GameplayHash||
            !ValidMouHg(parent)||use.SourceSeat!=actor.Seat||a.ActorSeat!=actor.Seat||a.ProviderSeat!=actor.Seat)throw new InvalidOperationException("Quzhou lost its exact original parent after issuing the native child.");
        var origin=new MouHgNativeUse(f.Id,id,a.ActionId,card.Id,shown.Kind,actor.Seat,targetSeat);f=parent;
        ReplaceRuntimeFrame(f.Id,f with{MouHuangGai=f.MouHuangGai! with{Native=origin}});UpdateLifecycleCardUse(id,u=>u with{MouHgQuzhou=origin});AdvanceEventRulesAndQueueFact(new MouHgNativeIssuedEvent(origin));
        SetCardUseStep(id,ResolutionFrameStep.ResolvingEffect);var attack=new CardAttackHandle(this,id,actor.Seat,targetSeat,card:card,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:shown.Kind,physicalCards:[card],ignoresArmor:HasCardArmorBypass(actor,target,shown.Kind),programSkillCardUseFrameId:f.Id);
        ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);
        if(_phase==TurnPhase.Play&&actor.Seat==_currentSeat&&LifecycleCardUse(id)!.UnlimitedUse!=true)RecordSlashUseDebit(id,actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,shown.Kind);CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id,shown.Kind,actor.Seat,targetSeat,IgnoresArmor:attack.IgnoresArmor));NotifyAiOfSlash(actor,target);
        if(!TryMarkProgramUseCommitted(id)||!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,[targetSeat],ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
    }
    private bool MouHgUseRidesOn(ProgramSkillFrame f,CardUseFrame u)=>ValidMouHg(f)&&f.MouHuangGai is{Stage:MouHgStage.NativeUse,Native:{}r,NativeReturned:false}&&u.MouHgQuzhou==r&&r.FrameId==f.Id&&r.UseId==u.Id&&u.CardId==r.CardId&&
        u.Action is{Type:CardActionType.Use}a&&a.ActionId==r.ActionId&&a.ProviderSeat==r.Actor&&a.PhysicalCards is[{CardId:var id,From:var from}]&&id==r.CardId&&from==CardLocation.Processing&&u.PhysicalCardIds is[var physical]&&physical==id&&
        f.MouHuangGai.Reveals.Any(e=>e.CardId==id&&e.Kind==r.Kind)&&IsSlashCard(u.CardKind)&&ShownEntityUseActorMatches(u,r.Actor,r.Actor)&&
        ProgramEventHistory<MouHgNativeIssuedEvent>().Count(e=>e.Origin==r)==1;
    [NativeUseValidation(35400)]private void ValidateMouHgUse(CardUseFrame use)
    {if(use.MouHgQuzhou is{}o&&(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.FrameId)is not{}f||!MouHgUseRidesOn(f,use)||use.Step!=ResolutionFrameStep.Completed))throw new InvalidOperationException("Quzhou cannot retire another or incomplete native Slash.");}
    [NativeUseReturn(35400)]private void ReturnMouHgUse(CardUseFrame use)
    {
        if(use.MouHgQuzhou is not{}o)return;var f=GetActiveProgramFrame(o.FrameId);var r=f.MouHuangGai!;
        if(!MouHgUseRidesOn(f,use)||r.NativeReturned||_resolutionStack.LastOrDefault()?.Id!=f.Id||LifecycleCardUse(use.Id)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1)throw new InvalidOperationException("Quzhou requires its once-completed original native Slash return.");
        ReplaceRuntimeTop(f with{MouHuangGai=r with{NativeReturned=true}});if(use.CardAttack is not{Active:true})SettleMouHgUse(o);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,35400)]private bool ReturnMouHgAttack(AttackCompletionReceipt completion)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouHuangGai:{Stage:MouHgStage.NativeUse,NativeReturned:true,Native:{}o}}f||completion.ResolutionId!=o.UseId)return false;
        if(completion.ProgramFrameId!=f.Id)throw new InvalidOperationException("Quzhou attack lost its original parent.");SettleMouHgUse(o);return true;
    }
    private void SettleMouHgUse(MouHgNativeUse origin)
    {
        var f=GetActiveProgramFrame(origin.FrameId);var r=f.MouHuangGai!;
        if(r.Native!=origin||!r.NativeReturned||LifecycleCardUse(origin.UseId)is not null||ProgramEventHistory<MouHgNativeReturnedEvent>().Any(e=>e.Origin==origin))throw new InvalidOperationException("Quzhou cannot return its original use twice.");
        ReplaceRuntimeTop(f with{MouHuangGai=r with{Stage=MouHgStage.NativeChildren,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new MouHgNativeReturnedEvent(origin));ContinueMouHg(f.Id);
    }
    private bool MouHgFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!ValidMouHg(f)||f.MouHuangGai is not{}r)return false;
        if(child is CardUseFrame use)return MouHgUseRidesOn(f,use);
        return child is CardsMovedTriggerWindowFrame moved&&f.PendingMovementContinuation is not null&&RuntimeSkills.RuntimeRules_Core_CommonRules.ProgramMovementReturnMatches(f,moved)&&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After);
    }
    private ProgramSkillFrame? MouHgObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.MouHgFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,35400)][ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,35400)][ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,35400)]
    private bool MouHgDamageObserver(long id)=>MouHgObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,35400)][ProgramDyingProof(ProgramDyingProofPhase.Invariant,35400)]private bool MouHgDying()=>ActiveDying is{}dying&&MouHgObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,35400)]private bool AdvanceMouHgChildren()=>_pendingDecision is null&&MouHgObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
