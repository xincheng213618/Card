namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27260)]
    private void ExpireQingyaActualTurnAlcohol(IGameEvent payload)
    {
        if(!HasYaDanCapability||payload is not ActualTurnCursorReturnedEvent ended||ended.ActualTurn!=_turnNumber)return;
        var history=CompleteProgramEventHistory().ToArray();
        var uses=history.OfType<QingyaNativeIssuedEvent>().Where(e=>e.Origin.ActualTurn==ended.ActualTurn&&e.Origin.Kind==CardKind.Alcohol)
            .Select(e=>e.Origin.UseId).ToHashSet();
        foreach(var latest in history.OfType<AlcoholAppliedEvent>().GroupBy(e=>e.SourceSeat).Select(g=>g.Last()))
            if(uses.Contains(latest.ResolutionId)&&IsValidPlayerSeat(latest.SourceSeat)&&_players[latest.SourceSeat].HasAlcoholEffect)
            {
                _players[latest.SourceSeat].HasAlcoholEffect=false;
                AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(latest.SourceSeat));
            }
    }
    private CardUseFrame? QingyaQualifiedUse(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||context.OwnerSeat!=c.OwnerSeat||
            context.CardUse is not{}cu||LifecycleCardUse(cu.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use}a}use||
            !IsSlashCard(use.CardKind)||a.ActorSeat!=c.OwnerSeat||use.SourceSeat!=c.OwnerSeat||a.ActionId!=cu.CardActionId||
            use.TargetSeats is not[var target]||target==c.OwnerSeat||!IsValidPlayerSeat(target)||!_players[target].IsAlive||!_players[c.OwnerSeat].IsAlive)return null;
        if(context.ParentFrameId!=0&&(_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||
            GetCardActionWindow(window)!=context.Window||window.ParentFrameId!=use.Id||window.Action.ActionId!=a.ActionId||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||ToSharedCandidate(window.Candidates[window.CandidateIndex])!=c))return null;
        return use;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27260)]
    private bool QualifyQingya(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>
        t.Effects is not[{Op:SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards}]||QingyaQualifiedUse(c,context)is not null;
    private IReadOnlyList<QingyaArc> QingyaArcs(int owner,int target,IReadOnlyList<int> living)
    {
        IReadOnlyList<int> Arc(int step)
        {
            var seats=new List<int>();var cursor=owner;
            for(var i=0;i<_players.Count;i++){cursor=(cursor+step+_players.Count)%_players.Count;if(cursor==target)break;if(living.Contains(cursor))seats.Add(cursor);}
            return Array.AsReadOnly(seats.ToArray());
        }
        var left=new QingyaArc(1,Arc(1));var right=new QingyaArc(-1,Arc(-1));
        return left.Seats.Count<right.Seats.Count?[left]:right.Seats.Count<left.Seats.Count?[right]:
            left.Seats.SequenceEqual(right.Seats)?[left]:[left,right];
    }
    private IReadOnlyList<QingyaHandMaterial> QingyaHandMaterials(int owner,int target)
    {
        if(!IsValidPlayerSeat(target)||!_players[target].IsAlive)return [];
        var from=CardLocation.Hand(target);
        return Array.AsReadOnly(GetHand(_players[target]).Select((card,slot)=>(Card:card,Slot:slot))
            .Where(x=>!IsForeignEquipmentDiscardPrevented(owner,x.Card,from,OwnedCardMoveIntent.Discard)&&!IsSelfHandCategoryDiscardForbidden(owner,x.Card,from,OwnedCardMoveIntent.Discard))
            .Select(x=>new QingyaHandMaterial(x.Card.Id,x.Card.Kind,target,x.Slot,CurrentHandEntry(target,x.Card.Id)??throw new InvalidOperationException("Qingya's original Hand entity lost its residency entry."))).ToArray());
    }
    private IReadOnlyList<QingyaDiscardEntity> QingyaGrantedEntities(QingyaGrantIssuedEvent grant)=>Array.AsReadOnly(
        ProgramEventHistory<QingyaHandDiscardedEvent>().Where(e=>e.FrameId==grant.GrantId).OrderBy(e=>e.Cursor)
            .Select(e=>new QingyaDiscardEntity(e.Material.CardId,e.Material.Kind,e.Material.HolderSeat,e.Sequence)).ToArray());
    private bool QingyaEntityStillAvailable(QingyaDiscardEntity entity)=>_cardZones.GetLocation(entity.CardId)==CardLocation.DiscardPile&&
        !_cardMovements.Any(m=>m.CardId==entity.CardId&&m.Sequence>entity.Sequence&&m.From==CardLocation.DiscardPile);
    private bool ValidQingyaGrant(QingyaGrantIssuedEvent g)=>g.GrantId>0&&g.Source.SkillId=="ol:qingya"&&g.Source.BindingId=="discard-between"&&
        IsValidPlayerSeat(g.Source.OwnerSeat)&&g.ActualTurn>0&&g.EntityCount>0&&g.HistoryCutoff>=0&&
        _contentRegistry.GetSkill(g.Source.SkillId).Program?.GameplayHash==g.GameplayHash&&
        ProgramEventHistory<YaDanStartedEvent>().Count(e=>e.FrameId==g.GrantId&&e.Source==g.Source&&e.Op==SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards&&
            e.GameplayHash==g.GameplayHash&&e.ActualTurn==g.ActualTurn&&e.IssuedPhase==g.IssuedPhase&&e.HistoryCutoff==g.HistoryCutoff)==1&&
        QingyaGrantedEntities(g).Count==g.EntityCount&&QingyaGrantedEntities(g).All(e=>_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&
            m.CardKind==e.Kind&&m.From==CardLocation.Hand(e.HolderSeat)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==QingyaDiscardReason));
    private bool QingyaGrantDue(QingyaGrantIssuedEvent grant,ActualDiscardRecoveryPhaseKey phase)
    {
        if(!ValidQingyaGrant(grant)||grant.ActualTurn!=_turnNumber||phase.TurnNumber!=grant.ActualTurn||
            phase.ActualTurnOwnerSeat!=_turnProgression.OwnerSeat||phase.Token==grant.IssuedPhase?.Token||
            ProgramEventHistory<ActualPhaseEndItemResolvedEvent>().Any(e=>e.EntitlementId==grant.GrantId)||
            ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==grant.ActualTurn))return false;
        var history=CompleteProgramEventHistory().ToArray();
        var started=history.Skip(grant.HistoryCutoff).OfType<ActualDiscardRecoveryPhaseStartedEvent>().Select(e=>e.Phase.Token).ToHashSet();
        if(!started.Contains(phase.Token))return false;
        var next=history.Skip(grant.HistoryCutoff).OfType<ActualPhaseEndingEvent>()
            .Select(e=>e.Phase).Where(p=>started.Contains(p.Token)&&p.TurnNumber==grant.ActualTurn&&p.ActualTurnOwnerSeat==phase.ActualTurnOwnerSeat&&p.Token!=grant.IssuedPhase?.Token)
            .Append(phase).First();
        return next==phase;
    }
    private IReadOnlyList<ActualPhaseEndItem> CollectQingyaPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase)
    {
        if(!HasYaDanCapability)return [];
        return Array.AsReadOnly(ProgramEventHistory<QingyaGrantIssuedEvent>().Where(g=>QingyaGrantDue(g,phase))
            .OrderBy(g=>g.GrantId).Select((g,index)=>{
                var trigger=_contentRegistry.GetSkill(g.Source.SkillId).Program!.Triggers.Single(t=>t.Effects is[{Op:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse}]);
                return new ActualPhaseEndItem(new(g.Source.OwnerSeat,g.Source.SkillId,trigger.Id,g.Source.SkillInstanceId,g.GameplayHash,trigger.Priority,index),g.GrantId);
            }).ToArray());
    }

    private bool IsIssuedQingyaCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(GetProgramTrigger(c).Effects is not[{Op:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse}]||
            context.ActualPhaseEnd is not{}due||!IsExactActualPhaseEndCandidate(c,context)||!_players[c.OwnerSeat].IsAlive)return false;
        var grant=ProgramEventHistory<QingyaGrantIssuedEvent>().SingleOrDefault(g=>g.GrantId==due.EntitlementId);
        return grant is not null&&grant.Source.OwnerSeat==c.OwnerSeat&&grant.Source.SkillId==c.SkillId&&
            grant.Source.SkillInstanceId==c.SkillInstanceId&&grant.GameplayHash==c.GameplayHash&&QingyaGrantDue(grant,due.Phase);
    }
}

