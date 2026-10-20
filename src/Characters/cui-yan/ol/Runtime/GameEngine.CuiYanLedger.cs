namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 10)]
    private bool CuiYanFinalizedAfterCategoryTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasCuiYanCapability;


    private bool HasCuiYanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShutongFinalizedUse)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZhijieActualTurnEnd);
    private bool CuiYanActualTurnOpen=>_setupComplete&&_turnNumber>0&&_turnProgression.TurnNumber==_turnNumber&&!ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.OwnerSeat==_turnProgression.OwnerSeat);
    // This is a skill-local semantic projection. Older consumers retain the
    // same-suit-multiple-material CaptureUsedCardSuit contract unchanged.
    private static Suit CuiYanTrueUseSuit(IReadOnlyList<int> ids,Suit? nativeSuit)=>ids.Count==1?nativeSuit??Suit.None:Suit.None;
    private bool IsCuiYanNextUseForbidden(int actorSeat,IReadOnlyList<int> materialIds,Suit? nativeEffectiveSuit)
    {
        if(!HasCuiYanCapability||!CuiYanActualTurnOpen)return false;
        var suit=CuiYanTrueUseSuit(materialIds,nativeEffectiveSuit);if(suit==Suit.None)return false;
        return OutstandingShutong(actorSeat).Any(g=>g.Suit==suit);
    }
    private IEnumerable<ShutongNextUseGrantedEvent> OutstandingShutong(int seat)=>ProgramEventHistory<ShutongNextUseGrantedEvent>().Where(g=>g.ActualTurn==_turnNumber&&g.TurnOwner==_turnProgression.OwnerSeat&&g.RecipientSeat==seat&&!ProgramEventHistory<ShutongNextUseConsumedEvent>().Any(c=>c.GrantFrameId==g.FrameId&&c.RecipientSeat==seat));
    private void ConsumeShutongForActualUse(long useId,CardActionContext? a,int actor,bool response)
    {
        var id=a?.ActionId??0;
        if(ProgramEventHistory<ShutongNextUseConsumedEvent>().Any(e=>e.UseId==useId&&e.ActionId==id&&e.RecipientSeat==actor&&e.ResponseUse==response))return;
        foreach(var grant in OutstandingShutong(actor).ToArray())
            AdvanceEventRulesAndQueueFact(new ShutongNextUseConsumedEvent(grant.FrameId,actor,useId,id,response,_turnNumber,_turnProgression.OwnerSeat));
    }
    private void CaptureCuiYanUseTargets(CardUseFrame use)
    {
        if(!HasCuiYanCapability||!CuiYanActualTurnOpen||!ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id)||use.Action is{Type:not CardActionType.Use})return;
        var old=ProgramEventHistory<CuiYanActualUseActorEvent>().LastOrDefault(e=>e.UseId==use.Id);
        var next=new CuiYanActualUseActorEvent(use.Id,use.Action?.ActionId??0,old?.ActualTurn??_turnNumber,old?.TurnOwner??_turnProgression.OwnerSeat,use.SourceSeat,use.CardKind);
        if(old!=next)AdvanceEventRulesAndQueueFact(next);
        var targets=(use.Action?.EffectiveDesignatedTargetSeats??use.TargetSeats).Distinct().ToArray();
        if(targets.Length==0&&!use.TargetsAdjusted&&(GetProgramCardCategory(use.CardKind)==SkillProgramCardCategory.Equipment||use.CardKind is CardKind.DrawTwo or CardKind.Peach or CardKind.Alcohol))targets=[use.SourceSeat];
        foreach(var target in targets.Where(IsValidPlayerSeat))
            if(!ProgramEventHistory<CuiYanEverTargetedEvent>().Any(e=>e.UseId==use.Id&&e.TargetSeat==target))AdvanceEventRulesAndQueueFact(new CuiYanEverTargetedEvent(use.Id,next.ActualTurn,next.TurnOwner,target));
    }
    private bool CuiYanTrueResponse(CardActionContext a,out long parentId)
    {
        parentId=0;if(a.Type!=CardActionType.Response||a.ActorSeat!=a.ProviderSeat||a.RequesterSeat is not null||a.ResponderSeat!=a.ActorSeat)return false;
        if(a.EffectiveKind==CardKind.Nullification&&_resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault()is{}n&&a.ParentActionId==NullificationParentActionId(n)&&a.OpponentSeat==n.SourceSeat){parentId=n.Id;return true;}
        if(a.EffectiveKind==CardKind.Dodge&&_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.Action?.ActionId==a.ParentActionId)is{CardAttack:{}attack}use&&attack.TargetSeat==a.ActorSeat&&a.OpponentSeat==attack.SourceSeat&&IsSlashCard(attack.EffectiveCardKind??use.CardKind)){parentId=use.Id;return true;}
        return false;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27470)]
    private void ObserveCuiYanCardHistory(IGameEvent payload)
    {
        if(!HasCuiYanCapability||!CuiYanActualTurnOpen||payload is CuiYanActualUseActorEvent or CuiYanEverTargetedEvent or CuiYanRespondedEvent or CuiYanEndQualifiedEvent or ShutongNextUseConsumedEvent)return;
        if(payload is CardUseDeclaredEvent d&&LifecycleCardUse(d.ResolutionId)is{}use&&use.SourceSeat==d.SourceSeat&&use.CardId==d.CardId&&use.CardKind==d.CardKind)
            ConsumeShutongForActualUse(use.Id,use.Action,use.SourceSeat,false);
        if(payload is CardActionAcceptedEvent{Action:{}a}&&CuiYanTrueResponse(a,out var native))ConsumeShutongForActualUse(native,a,a.ActorSeat,true);
        if(payload is CardRespondedEvent response&&!ProgramEventHistory<CuiYanRespondedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.ResponderSeat==response.ResponderSeat))AdvanceEventRulesAndQueueFact(new CuiYanRespondedEvent(_turnNumber,_turnProgression.OwnerSeat,response.ResponderSeat));
        if(payload is TurnEndedEvent)
        {
            foreach(var owner in _players.Where(p=>p.IsAlive))
                if(!ProgramEventHistory<CuiYanEndQualifiedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.OwnerSeat==owner.Seat))
                    AdvanceEventRulesAndQueueFact(new CuiYanEndQualifiedEvent(_turnNumber,_turnProgression.OwnerSeat,owner.Seat,CuiYanUsedOrPlayed(owner.Seat),ProgramEventHistory<CuiYanEverTargetedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.TargetSeat==owner.Seat)));
        }
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27470)]
    private void CaptureCuiYanNativeHistory(IGameEvent payload)
    {
        if(!HasCuiYanCapability||payload is CuiYanActualUseActorEvent or CuiYanEverTargetedEvent or CuiYanRespondedEvent or CuiYanEndQualifiedEvent or ShutongNextUseConsumedEvent)return;
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().ToArray())CaptureCuiYanUseTargets(use);
    }
    [NativeUsePreparation(27470)]private bool CaptureCuiYanFinalUse(CardUseFrame use){CaptureCuiYanUseTargets(use);return false;}
    private bool CuiYanUsedOrPlayed(int seat)=>ProgramEventHistory<CuiYanRespondedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.ResponderSeat==seat)||ProgramEventHistory<CuiYanActualUseActorEvent>().Where(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat).GroupBy(e=>e.UseId).Any(g=>g.Last().ActorSeat==seat);
}
