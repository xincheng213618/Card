namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool _hasIssuedZhugeGuoState;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 90)]
    private void CaptureQirangDeclaredUse(IGameEvent payload)
    {
        if(!_hasIssuedZhugeGuoState&&!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse))return;
        if(payload is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId)is not{Action:{Type:CardActionType.Use}a}use)return;
        if(EquipmentCatalog.IsEquipment(a.EffectiveKind) && a.PhysicalCards is[var material] && _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse) && CollectProgramTriggerCandidates(_players[a.ActorSeat],SkillProgramTriggerWindow.CardUseTargetsFinalized).Any(c=>ProgramInstructionResolver.Default.Features(GetProgramTrigger(c)).HasOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse)))
        {
            var origin=new QirangEquipmentOrigin(use.Id,a,_movementSequence);ReplaceRuntimeFrame(use.Id,use=use with{QirangEquipmentOrigin=origin});AdvanceEventRulesAndQueueFact(new QirangEquipmentOriginCapturedEvent(use.Id,a.ActionId,a.ActorSeat,material.CardId,origin.MovementBefore));
        }
        if(!_hasIssuedZhugeGuoState || !IsOrdinaryTrick(a.EffectiveKind) || a.ActorSeat!=a.ProviderSeat || a.PhysicalCards is not[var cost] || cost.From!=CardLocation.Hand(a.ActorSeat) || _cardZones.GetLocation(cost.CardId)!=cost.From || _players[a.ActorSeat].QirangEntityLeases is not{Count:>0}leases)return;
        var lease=leases.FirstOrDefault(l=>l.CardId==cost.CardId&&l.PrintedKind==a.EffectiveKind&&l.OwnerSeat==a.ActorSeat&&HasRuntimeSkillInstance(_players[a.ActorSeat],l.Source.SkillId,l.Source.SkillInstanceId));if(lease is null)return;
        var q=new QirangUseQualification(lease,use.Id,a,_movementSequence);ReplaceRuntimeFrame(use.Id,use with{QirangQualification=q});AdvanceEventRulesAndQueueFact(new QirangUseQualifiedEvent(use.Id,a.ActionId,a.ActorSeat,cost.CardId,lease.GainSequence,q.MovementBefore));
    }
    private bool ValidQirangQualification(QirangUseQualification q)
    {
        var l=q.Lease;var a=q.OriginalAction;
        return l.Source.OwnerSeat==l.OwnerSeat&&a.ActorSeat==l.OwnerSeat&&a.ProviderSeat==l.OwnerSeat&&a.Type==CardActionType.Use&&a.PhysicalCards is[var physical]&&physical.CardId==l.CardId&&physical.From==CardLocation.Hand(l.OwnerSeat)&&physical.CardKind==l.PrintedKind&&a.EffectiveKind==l.PrintedKind&&IsOrdinaryTrick(l.PrintedKind)&&l.GainSequence>0&&q.MovementBefore>=l.GainSequence&&_contentRegistry.GetSkill(l.Source.SkillId).Program?.GameplayHash==l.GameplayHash&&
            CompleteProgramEventHistory().OfType<QirangEntityLeaseIssuedEvent>().Count(e=>e.Lease==l)==1&&CompleteProgramEventHistory().OfType<QirangUseQualifiedEvent>().Count(e=>e==new QirangUseQualifiedEvent(q.CardUseFrameId,a.ActionId,l.OwnerSeat,l.CardId,l.GainSequence,q.MovementBefore))==1&&
            _cardMovements.Count(m=>m.CardId==l.CardId&&m.Sequence==l.GainSequence&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(l.OwnerSeat)&&m.Reason.Value==QirangGainReason)==1&&
            _cardMovements.Count(m=>m.CardId==l.CardId&&m.Sequence>q.MovementBefore&&m.From==physical.From&&m.To==CardLocation.Processing&&m.Reason==(a.EffectiveKind==CardKind.IronChain?CardMoveReasons.IronChainUse:CardMoveReasons.Use))==1;
    }
    private void ObserveZhugeGuoMovement(CardMovementRecord movement)
    {
        if(!_hasIssuedZhugeGuoState || movement.From is not{Zone:CardZoneKind.Hand,OwnerSeat:{}owner} || movement.From==movement.To || _players[owner].QirangEntityLeases is not{Count:>0}leases)return;
        foreach(var l in leases.Where(l=>l.CardId==movement.CardId))AdvanceEventRulesAndQueueFact(new QirangEntityLeaseEndedEvent(owner,l.CardId,l.GainSequence,"left-owner-hand"));
        var remaining=leases.Where(l=>l.CardId!=movement.CardId).ToArray();_players[owner].QirangEntityLeases=remaining.Length==0?null:remaining;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 100)]
    private void ObserveZhugeGuoTurnAndDeath(IGameEvent payload)
    {
        if(!_hasIssuedZhugeGuoState)return;
        if(payload is TurnStartedEvent turn)
        {
            var owner=_players[turn.ActorSeat];var eligible=(owner.YuhuaDelayDebts??[]).Where(d=>d.CreatedActualTurn<turn.TurnNumber).ToArray();
            if(eligible.Length==0&&owner.YuhuaActiveQuota is null)return;
            var count=Math.Min(5,1+eligible.Length);var remaining=(owner.YuhuaDelayDebts??[]).Except(eligible).ToArray();owner.YuhuaDelayDebts=remaining.Length==0?null:remaining;owner.YuhuaActiveQuota=new(owner.Seat,turn.TurnNumber,count);AdvanceEventRulesAndQueueFact(new YuhuaActualTurnQuotaStartedEvent(owner.Seat,turn.TurnNumber,count));
        }
        else if(payload is PlayerDiedEvent died)
        {
            var owner=_players[died.VictimSeat];foreach(var l in owner.QirangEntityLeases??[])AdvanceEventRulesAndQueueFact(new QirangEntityLeaseEndedEvent(owner.Seat,l.CardId,l.GainSequence,"owner-died"));owner.QirangEntityLeases=null;owner.YuhuaDelayDebts=null;owner.YuhuaActiveQuota=null;
        }
    }
    private CardSnapshot[] GetZhugeGuoPrivatelyViewedCards(int viewer)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{ZhugeGuo.Stage:ZhugeGuoStage.YuhuaInspect,TopReorder:{}top}f&&f.OwnerSeat==viewer&&_pendingDecision is{Kind:DecisionKind.ProgramTopReorder,PlayerSeat:var seat}&&seat==viewer?top.ViewedCardIds.Select(id=>ToSnapshot(_cardZones.CardsAt(CardLocation.DrawPile).Single(c=>c.Id==id))).ToArray():[];
    private bool ZhugeGuoFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(!CanContinueZhugeGuo(root)||root.ZhugeGuo is not{Stage:ZhugeGuoStage.GainChildren}r||child is not CardsMovedTriggerWindowFrame moved)return false;
        return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.ResumeProgramFrameId is null&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==r.GainedCardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==QirangGainReason);
    }
    private ProgramSkillFrame? ZhugeGuoObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.ZhugeGuoFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool HasZhugeGuoDamageObserver(long id)=>ZhugeGuoObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool HasZhugeGuoBeforeDamageObserver(long id)=>ZhugeGuoObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool IsZhugeGuoDying()=>ActiveDying is{}dying&&ZhugeGuoObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool AllowsZhugeGuoNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||ZhugeGuoObserverRoot()is not{}root||root.Id==observer.Id)return false;
        var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 200)]
    private bool TryAdvanceZhugeGuoSubtree()=>_pendingDecision is null&&ZhugeGuoObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
    private void MarkYuhuaTopPlacement(ProgramSkillFrame supplied,ProgramTopReorder state)
    {
        if(supplied.ZhugeGuo is not{Stage:ZhugeGuoStage.YuhuaInspect}r)return;
        var f=GetActiveProgramFrame(supplied.Id);RequireZhugeGuo(f);
        var selected=state.TopCardIds.Concat(state.BottomCardIds).ToArray();
        if(r.PlacementIssued||!state.ViewedCardIds.SequenceEqual(r.Viewed)||selected.Length!=r.Viewed.Count||selected.Distinct().Count()!=selected.Length||selected.Any(id=>!r.Viewed.Contains(id))||!_cardZones.CardsAt(CardLocation.DrawPile).TakeLast(state.TopCardIds.Count).Reverse().Select(c=>c.Id).SequenceEqual(state.TopCardIds)||!_cardZones.CardsAt(CardLocation.DrawPile).Take(state.BottomCardIds.Count).Select(c=>c.Id).SequenceEqual(state.BottomCardIds))throw new InvalidOperationException("Yuhua placement requires exactly the original private entities and real native top/bottom partition.");
        ReplaceRuntimeTop(f with{ZhugeGuo=r with{PlacementIssued=true,PlacedTop=state.TopCardIds,PlacedBottom=state.BottomCardIds}});AdvanceEventRulesAndQueueFact(new YuhuaPrivatePeekPlacedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,state.TopCardIds.Count,state.BottomCardIds.Count));
    }
}
