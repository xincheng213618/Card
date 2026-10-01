namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<IGameEvent> ResponseExchangeHistory() => _events.Select(e=>e.Payload).Concat(_pendingEvents);
    private bool HasResponseEntityExchangeObservers() => _players.Any(p=>p.IsAlive&&(GetSkillBindingShard(p)?.GetInstanceTriggers(SkillProgramTriggerWindow.CardResponseAccepted)??[]).Any(b=>b.Trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.ExchangeRespondedCardEntities)));
    private IReadOnlyList<ProgramResponseExchangeStateSnapshot>? GetResponseExchangeStateSnapshots()
    {
        var states=_players.SelectMany(p=>(GetSkillBindingShard(p)?.GetInstanceTriggers(SkillProgramTriggerWindow.CardResponseAccepted)??[]).SelectMany(b=>b.Trigger.Effects.Where(e=>e.Op==SkillProgramEffectOp.ExchangeRespondedCardEntities).Select(e=>new ProgramResponseExchangeStateSnapshot(p.Seat,b.SkillId,b.SkillInstanceId,e.StateId!,ResponseExchangeUpgraded(p.Seat,b.SkillId,b.SkillInstanceId,e.StateId!))))).Distinct().ToArray();
        return states.Length==0?null:Array.AsReadOnly(states);
    }
    private IReadOnlyList<TurnProhibitedPhysicalCardsSnapshot>? GetResponseEntityRestrictionSnapshots()
    {
        var states=ResponseExchangeHistory().OfType<ProgramResponseEntityClaimedEvent>().Where(e=>e.Restricted&&e.TurnNumber==_turnNumber&&e.TurnSeat==_currentSeat).Select(e=>new TurnProhibitedPhysicalCardsSnapshot(e.RecipientSeat,e.OwnerSeat,e.SkillId,e.SkillInstanceId,Array.AsReadOnly(e.CardIds.ToArray()),e.TurnNumber,e.TurnSeat)).ToArray();
        return states.Length==0?null:Array.AsReadOnly(states);
    }
    private bool ResponseExchangeUpgraded(int owner,string skill,string instance,string state) => ResponseExchangeHistory().OfType<ProgramResponseExchangeUpgradedEvent>().Any(e=>e.OwnerSeat==owner&&e.SkillId==skill&&e.SkillInstanceId==instance&&e.StateId==state);
    private bool IsExchangedCardClaim(long actionId,int cardId) => ResponseExchangeHistory().OfType<ProgramResponseEntityClaimedEvent>().Any(e=>e.ClaimedActionId==actionId&&e.CardIds.Contains(cardId));
    private bool IsExchangedUseCardClaim(long frameId,int cardId) => _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f=>f.Id==frameId)?.Action is {} action && IsExchangedCardClaim(action.ActionId,cardId);
    private bool IsResponseEntityRestricted(int seat,int id) => ResponseExchangeHistory().OfType<ProgramResponseEntityClaimedEvent>().Any(e=>e.Restricted&&e.RecipientSeat==seat&&e.TurnNumber==_turnNumber&&e.TurnSeat==_currentSeat&&e.CardIds.Contains(id));
    private Card EntityAtCurrentLocation(int id) => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id);
    private CardActionContext? ActualRespondedAction(CardActionContext response)
    {
        if(response.Type!=CardActionType.Response || response.ParentActionId is not {} parentId) return null;
        // The old ParentActionId remains the outer trick for compatibility. In a
        // counterspell chain, the directly answered action is the prior counterspell.
        if(response.EffectiveKind==CardKind.Nullification&&ActiveNullificationWindow is {ChainDepth:>1})
            return ResponseExchangeHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).LastOrDefault(a=>a.ActionId!=response.ActionId&&a.Type==CardActionType.Response&&a.EffectiveKind==CardKind.Nullification&&a.ParentActionId==parentId);
        return ResponseExchangeHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).LastOrDefault(a=>a.ActionId==parentId);
    }
    private bool CanExchangeResponseEntities(int owner,string skill,string instance,string state,CardActionContext response)
    {
        var original=ActualRespondedAction(response);
        if(original is null || original.ActorSeat!=owner || response.ActorSeat==owner ||
            original.Type!=CardActionType.Use && original.EffectiveKind!=CardKind.Nullification || response.PhysicalCards.Count==0 ||
            response.PhysicalCards.Any(c=>IsExchangedCardClaim(response.ActionId,c.CardId)||_cardZones.GetLocation(c.CardId).Zone is not(CardZoneKind.Processing or CardZoneKind.DiscardPile))) return false;
        if(ResponseExchangeUpgraded(owner,skill,instance,state)) return true;
        return _players[response.ActorSeat].IsAlive&&original.PhysicalCards.Count>0&&original.PhysicalCards.All(c=>!IsExchangedCardClaim(original.ActionId,c.CardId)&&_cardZones.GetLocation(c.CardId).Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile);
    }
    private bool CanOfferResponseExchange(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        var effect=trigger.Effects.FirstOrDefault(e=>e.Op==SkillProgramEffectOp.ExchangeRespondedCardEntities);
        if(effect is null) return true;
        var response=ResponseExchangeHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).LastOrDefault(a=>a.ActionId==context.CardUse?.CardActionId);
        return response is not null&&CanExchangeResponseEntities(candidate.OwnerSeat,candidate.SkillId,candidate.SkillInstanceId,effect.StateId!,response);
    }
    private SkillProgramStepOutcome BeginResponseEntityExchange(ProgramSkillFrame frame,string state)
    {
        var window=_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==frame.WindowContext!.ParentFrameId);
        var response=window.Action;var original=ActualRespondedAction(response);
        if(original is null||!CanExchangeResponseEntities(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,state,response)) return SkillProgramStepOutcome.Continue;
        var originals=ResponseExchangeUpgraded(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,state)?[]:original.PhysicalCards.Select(c=>c.CardId).ToArray();
        ReplaceRuntimeTop(frame=frame with {ResponseEntityExchange=new(original.ActionId,response.ActionId,response.ActorSeat,originals,response.PhysicalCards.Select(c=>c.CardId).ToArray(),state,1)});
        ClaimResponseEntities(frame,original.ActionId,response.ActorSeat,originals,true);
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void ClaimResponseEntities(ProgramSkillFrame frame,long action,int recipient,IReadOnlyList<int> ids,bool restricted)
    {
        if(ids.Count==0||!_players[recipient].IsAlive) return;
        var draft=frame.ResponseEntityExchange!;
        if(ids.Any(id=>IsExchangedCardClaim(action,id)||_cardZones.GetLocation(id).Zone is not(CardZoneKind.Processing or CardZoneKind.DiscardPile))) throw new InvalidOperationException("A response entity exchange lost its exact public cost.");
        AdvanceEventRulesAndQueueFact(new ProgramResponseEntityClaimedEvent(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,draft.RespondedActionId,draft.ResponseActionId,action,recipient,ids.ToArray(),_turnNumber,_currentSeat,restricted));
        foreach(var id in ids) MoveCard(EntityAtCurrentLocation(id),_cardZones.GetLocation(id),CardLocation.Hand(recipient),new CardMoveReason("skill-program.response-entity-exchange"));
    }
    private SkillProgramStepOutcome BeginPublicSuitEscalatingDiscard(ProgramSkillFrame frame,SkillProgramEffect effect)
    {
        var target=frame.SelectedTargetSeats.Single();
        if(target==frame.OwnerSeat||!_players[target].IsAlive) return SkillProgramStepOutcome.Continue;
        var previous=ResponseExchangeHistory().OfType<ProgramEscalatingDiscardStartedEvent>().Count(e=>e.OwnerSeat==frame.OwnerSeat&&e.SkillId==frame.SkillId&&e.StateId==effect.StateId);
        var draw=_players.Where(p=>p.IsAlive).Sum(p=>GetEquipment(p).Concat(GetJudgment(p)).Count(c=>EffectiveSuit(p,c)==effect.Suits.Single()));
        AdvanceEventRulesAndQueueFact(new ProgramEscalatingDiscardStartedEvent(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,effect.StateId!,previous,target,draw));
        ReplaceRuntimeTop(frame=frame with {PublicSuitDiscard=new(target,previous,effect.StateId!,effect.SkillIds.Single(),1)});
        DrawCards(_players[target],draw,true,new CardMoveReason("skill-program.public-suit-escalating-draw"));
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool TryResumeResponseEntityExchange(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame||frame.Id!=id||frame.PendingMovementContinuation is not null) return false;
        if(frame.ResponseEntityExchange is {} exchange)
        {
            if(exchange.Stage==1)
            {
                ReplaceRuntimeTop(frame=frame with {ResponseEntityExchange=exchange with {Stage=2}});
                if(_players[frame.OwnerSeat].IsAlive) ClaimResponseEntities(frame,exchange.ResponseActionId,frame.OwnerSeat,exchange.ResponseCardIds.Where(cardId=>!IsExchangedCardClaim(exchange.ResponseActionId,cardId)&&_cardZones.GetLocation(cardId).Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile).ToArray(),false);
                if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            }
            else {ReplaceRuntimeTop(frame with {ResponseEntityExchange=null});AdvanceRuntimeProgram(frame.Id);}
            return true;
        }
        if(frame.PublicSuitDiscard is {} discard)
        {
            if(discard.Stage==1)
            {
                var cards=GetHand(_players[discard.TargetSeat]).Concat(GetEquipment(_players[discard.TargetSeat])).ToArray();
                if(!_players[discard.TargetSeat].IsAlive||discard.Required==0||cards.Length==0){FinishPublicSuitDiscard(frame);return true;}
                ReplaceRuntimeTop(frame=frame with {PublicSuitDiscard=discard with {Required=Math.Min(discard.Required,cards.Length),Stage=2,SelectedIds=[]}});
                PublishPublicSuitDiscard(frame);return true;
            }
            if(discard.Stage==2){PublishPublicSuitDiscard(frame);return true;}
            FinishPublicSuitDiscard(frame);return true;
        }
        return false;
    }
    private void FinishPublicSuitDiscard(ProgramSkillFrame frame)
    {
        var draft=frame.PublicSuitDiscard!;
        if(draft.Exhausted)
        {
            var sourceIds=_players[frame.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId==frame.SkillId&&g.SkillInstanceId==frame.SkillInstanceId).Select(g=>g.SourceId).ToHashSet();
            foreach(var grant in _players[frame.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId==draft.UpgradeSkillId&&g.IsEnabled&&sourceIds.Contains(g.SourceId)))
                AdvanceEventRulesAndQueueFact(new ProgramResponseExchangeUpgradedEvent(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,grant.SkillId,grant.SkillInstanceId,draft.StateId));
            foreach(var grant in _players[frame.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId==frame.SkillId&&g.SkillInstanceId==frame.SkillInstanceId)) _players[frame.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
        }
        ReplaceRuntimeTop(frame with {PublicSuitDiscard=null});AdvanceRuntimeProgram(frame.Id);
    }
    private void PublishPublicSuitDiscard(ProgramSkillFrame frame)
    {
        var draft=frame.PublicSuitDiscard!;var skill=_contentRegistry.GetSkill(frame.SkillId);
        var cards=GetHand(_players[draft.TargetSeat]).Concat(GetEquipment(_players[draft.TargetSeat])).Where(c=>!draft.SelectedIds!.Contains(c.Id)).ToArray();
        var choices=cards.Select(c=>new PromptChoice(new ChoiceId($"public-suit-discard.{frame.Id}.{c.Id}"),$"弃置【{c.DisplayName}】{GetSuitDisplayName(c.Suit)}{c.RankText}",[c.Id],[],new Dictionary<string,string>{["program-action"]="public-suit-discard"})).ToArray();
        _pendingDecision=new(DecisionKind.ProgramTrigger,draft.TargetSeat,$"{skill.Name}：选择弃置 {draft.Required} 张手牌或装备牌。",cards.Select(c=>c.Id).ToArray(),[],draft.TargetSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[draft.TargetSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void ResolvePublicSuitDiscard(PromptChoice choice)
    {
        var frame=(ProgramSkillFrame)_resolutionStack[^1];var draft=frame.PublicSuitDiscard!;var id=choice.Cards.Single();
        var available=GetHand(_players[draft.TargetSeat]).Concat(GetEquipment(_players[draft.TargetSeat])).ToArray();
        if(!_players[draft.TargetSeat].IsAlive||available.All(c=>c.Id!=id)||draft.SelectedIds!.Contains(id)) throw new InvalidOperationException("The selected escalating discard is no longer owned.");
        ClearPendingDecision();var selected=draft.SelectedIds!.Append(id).ToArray();
        if(selected.Length<draft.Required){ReplaceRuntimeTop(frame with {PublicSuitDiscard=draft with {SelectedIds=selected}});PublishPublicSuitDiscard((ProgramSkillFrame)_resolutionStack[^1]);return;}
        ReplaceRuntimeTop(frame=frame with {PublicSuitDiscard=draft with {Stage=3,SelectedIds=selected,Exhausted=selected.Length>0&&selected.Length==available.Length}});
        foreach(var cardId in selected) MoveCard(EntityAtCurrentLocation(cardId),_cardZones.GetLocation(cardId),CardLocation.DiscardPile,new CardMoveReason("skill-program.public-suit-escalating-discard"));
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
    private bool TryGetResponseExchangeMovementDyingCosts(ProgramCardTriggerWindowFrame? window,out int[] costs)
    {
        costs=[];
        if(window is not{Continuation:ProgramCardContinuation.NullificationResponse,Action.Type:CardActionType.Response,Action.EffectiveKind:CardKind.Nullification}||
            ActiveNullificationWindow is not{} pending||window.ParentFrameId!=pending.Id||
            ActiveDying is not{Continuation:DyingContinuationKind.ProgramSkill,KillerSeat:null} dying) return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==window.Id);
        if(i<0||i+4>=_resolutionStack.Count||_resolutionStack[i+1] is not ProgramSkillFrame exchange||exchange.WindowContext?.ParentFrameId!=window.Id||
            exchange.ResponseEntityExchange is not{} draft||draft.ResponseActionId!=window.Action.ActionId||exchange.PendingMovementContinuation is not{} movementPending||movementPending.SubjectSeat!=exchange.OwnerSeat||movementPending.CoverageResultBind is not null||
            _resolutionStack[i+2] is not CardsMovedTriggerWindowFrame movement||movement.Batch.ParentFrameId!=exchange.Id||movement.ResumeProgramFrameId is {} resume&&resume!=exchange.Id||movement.Batch.OriginSkillId!=exchange.SkillId||movement.Batch.OriginSkillInstanceId!=exchange.SkillInstanceId||movement.Batch.OriginOwnerSeat!=exchange.OwnerSeat||
            movement.Batch.Movements.Count==0||movement.Batch.Movements.Any(m=>m.Reason.Value!="skill-program.response-entity-exchange"||!draft.OriginalCardIds.Concat(draft.ResponseCardIds).Contains(m.CardId))||
            _resolutionStack[i+3] is not ProgramSkillFrame program||program.WindowContext is not{} context||context.ParentFrameId!=movement.Id||context.MovementBatch?.Id!=movement.Batch.Id||context.Window is not(SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained)||program.Id!=dying.ParentFrameId||
            _resolutionStack[i+4] is not DyingFrame child||child.Id!=dying.FrameId||child.ParentFrameId!=program.Id||child.VictimSeat!=dying.VictimSeat||child.KillerSeat!=dying.KillerSeat||child.ResponderIndex!=dying.ResponderIndex||!child.ResponderSeats.SequenceEqual(dying.ResponderSeats)) return false;
        var paid=ProgramInstructionResolver.Default.Resolve(exchange,_contentRegistry.GetSkill(exchange.SkillId).Program!).GetPausedInstruction(exchange.InstructionIndex).Effect;
        var effect=ProgramInstructionResolver.Default.Resolve(program,_contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
        if(paid.Op!=SkillProgramEffectOp.ExchangeRespondedCardEntities||effect.Op!=SkillProgramEffectOp.LoseHp||ResolveProgramEffectTarget(program,effect.Target)!=dying.VictimSeat) return false;
        if(i+5<_resolutionStack.Count&&_resolutionStack[i+5] is CardUseFrame rescue)
        {
            if(rescue.DyingResponse is not{} response||response.ResolutionId!=dying.FrameId||response.ResponderSeat!=dying.ResponderSeat||rescue.SourceSeat!=response.ResponderSeat||
                rescue.Action is not{Type:CardActionType.Use} action||action.ActorSeat!=rescue.SourceSeat||action.ProviderSeat!=rescue.SourceSeat||!rescue.TargetSeats.SequenceEqual([dying.VictimSeat])||rescue.CardKind is not(CardKind.Peach or CardKind.Alcohol)||action.EffectiveKind!=rescue.CardKind||!action.EffectiveDesignatedTargetSeats.SequenceEqual(rescue.TargetSeats)||!action.PhysicalCards.Select(c=>c.CardId).SequenceEqual(rescue.PhysicalCardIds??[rescue.CardId])||
                (rescue.CardKind==CardKind.Peach?!response.UsedPeach:!response.UsedAlcohol||rescue.SourceSeat!=dying.VictimSeat)) return false;
            costs=(rescue.PhysicalCardIds??[rescue.CardId]).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).ToArray();
        }
        return true;
    }
    private void AssertResponseEntityExchange(ProgramSkillFrame frame,SkillProgramEffect paused)
    {
        if(frame.ResponseEntityExchange is {} exchange)
        {
            var window=_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==frame.WindowContext?.ParentFrameId);
            var original=window is null?null:ActualRespondedAction(window.Action);
            if(paused.Op!=SkillProgramEffectOp.ExchangeRespondedCardEntities||exchange.StateId!=paused.StateId||exchange.Stage is not(1 or 2)||window is null||original is null||original.ActionId!=exchange.RespondedActionId||original.ActorSeat!=frame.OwnerSeat||window.Action.ActionId!=exchange.ResponseActionId||window.Action.ActorSeat!=exchange.RecipientSeat||
                !window.Action.PhysicalCards.Select(c=>c.CardId).SequenceEqual(exchange.ResponseCardIds)||exchange.OriginalCardIds.Count>0&&!original.PhysicalCards.Select(c=>c.CardId).SequenceEqual(exchange.OriginalCardIds)||exchange.OriginalCardIds.Count==0&&!ResponseExchangeUpgraded(frame.OwnerSeat,frame.SkillId,frame.SkillInstanceId,exchange.StateId))
                throw new InvalidOperationException("An entity exchange lost its exact responded/response identity or upgrade source.");
        }
        if(frame.PublicSuitDiscard is {} discard)
        {
            if(paused.Op!=SkillProgramEffectOp.DrawPublicSuitThenEscalatingDiscard||discard.StateId!=paused.StateId||discard.UpgradeSkillId!=paused.SkillIds.Single()||discard.TargetSeat==frame.OwnerSeat||!frame.SelectedTargetSeats.SequenceEqual([discard.TargetSeat])||discard.Required<0||discard.Stage is <1 or >3||discard.SelectedIds is {} selected&&(selected.Distinct().Count()!=selected.Count||selected.Count>discard.Required)||discard.Exhausted&&(discard.Stage!=3||discard.SelectedIds is not{Count:>0}))
                throw new InvalidOperationException("An escalating discard lost its real participant, source or paid set.");
            if(discard.Stage==2&&ReferenceEquals(frame,_resolutionStack.LastOrDefault())&&(_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:true} prompt||prompt.PlayerSeat!=discard.TargetSeat||prompt.Choices.Count==0||prompt.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")!="public-suit-discard")))
                throw new InvalidOperationException("An escalating discard lost its private payer choice.");
        }
    }
    private sealed partial class ProgramSkillHost : IResponseExchangeProgramHost
    {
        public SkillProgramStepOutcome ExchangeRespondedCardEntities(ProgramSkillFrame frame,string stateId)=>engine.BeginResponseEntityExchange(frame,stateId);
        public SkillProgramStepOutcome DrawPublicSuitThenEscalatingDiscard(ProgramSkillFrame frame,SkillProgramEffect effect)=>engine.BeginPublicSuitEscalatingDiscard(frame,effect);
    }
}
