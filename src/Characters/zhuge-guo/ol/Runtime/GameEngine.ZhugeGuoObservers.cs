namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool _hasIssuedZhugeGuoState;
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
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 40)]
    private CardSnapshot[] GetZhugeGuoPrivatelyViewedCards(int viewer)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{ZhugeGuo.Stage:ZhugeGuoStage.YuhuaInspect,TopReorder:{}top}f&&f.OwnerSeat==viewer&&_pendingDecision is{Kind:DecisionKind.ProgramTopReorder,PlayerSeat:var seat}&&seat==viewer?top.ViewedCardIds.Select(id=>ToSnapshot(_cardZones.CardsAt(CardLocation.DrawPile).Single(c=>c.Id==id))).ToArray():[];
    private bool ZhugeGuoFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(!CanContinueZhugeGuo(root)||root.ZhugeGuo is not{Stage:ZhugeGuoStage.GainChildren}r||child is not CardsMovedTriggerWindowFrame moved)return false;
        return moved.Batch.ParentFrameId==root.Id&&moved.Batch.AwaitingProgramFrameId==root.Id&&moved.ResumeProgramFrameId is null&&moved.Batch.OriginOwnerSeat==root.OwnerSeat&&moved.Batch.OriginSkillId==root.SkillId&&moved.Batch.OriginSkillInstanceId==root.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==r.GainedCardId&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(root.OwnerSeat)&&m.Reason.Value==QirangGainReason);
    }
    private ProgramSkillFrame? ZhugeGuoObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.ZhugeGuoFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 110)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 220)]
    private bool HasZhugeGuoDamageObserver(long id)=>ZhugeGuoObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 310)]
    private bool HasZhugeGuoBeforeDamageObserver(long id)=>ZhugeGuoObserverRoot()is{}r&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 70)]
    private bool IsZhugeGuoDying()=>ActiveDying is{}dying&&ZhugeGuoObserverRoot()is{}r&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 440)]
    private bool AllowsZhugeGuoNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess||observer.AttackAttempt is not null||observer.InstructionIndex<1||_resolutionStack.LastOrDefault()?.Id!=observer.Id||observer.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)||ZhugeGuoObserverRoot()is not{}root||root.Id==observer.Id)return false;
        var e=GetPausedPrivateOfferEffect(observer);return e is{Op:SkillProgramEffectOp.Damage}&&amount==e.Amount&&source==e.ActorReference&&nature==e.DamageNature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 200)]
    private bool TryAdvanceZhugeGuoSubtree()=>_pendingDecision is null&&ZhugeGuoObserverRoot()is not null&&TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
}
