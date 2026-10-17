namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<RuanYuMiaoxianPaymentSource>? CaptureRuanYuMiaoxianPaymentSources(IEnumerable<CardLocation> locations)
    {
        if(!HasRuanYuCapability)return null;var result=new List<RuanYuMiaoxianPaymentSource>();
        foreach(var seat in locations.Where(l=>l.OwnerSeat is not null&&l.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain).Select(l=>l.OwnerSeat!.Value).Distinct())
        {
            var owner=_players[seat];if(!owner.IsAlive)continue;var red=GetHand(owner).Where(c=>SuitColor(EffectiveSuit(owner,c))==true).ToArray();var black=GetHand(owner).Where(c=>SuitColor(EffectiveSuit(owner,c))==false).ToArray();
            var grain=red.Length==0?_cardZones.CardsAt(CardLocation.WoodenOxGrain(seat)).Where(c=>SuitColor(EffectiveSuit(owner,c))==true).Select(c=>c.Id).ToArray():[];
            foreach(var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i=>i.SkillId=="ol:miaoxian"&&i.Program.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw))))
                result.Add(new(new(instance.SkillId,"unique-red-use",seat,instance.SkillInstanceId),instance.Program.GameplayHash,red.Length,red.Length==1?red[0].Id:null,black.Length,black.Length==1?black[0].Id:null){RedGrainIds=grain});
        }
        return result.Count==0?null:Array.AsReadOnly(result.ToArray());
    }
    private void CaptureRuanYuMiaoxianPaymentBatch(CardMovementBatchContext batch,IReadOnlyList<RuanYuMiaoxianPaymentSource>? snapshots)
    {
        if(!HasRuanYuCapability)return;
        foreach(var original in snapshots??[])foreach(var m in batch.Movements.Where(m=>m.From.OwnerSeat==original.Source.OwnerSeat&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain&&(m.To==CardLocation.Processing||m.To==CardLocation.DrawPile)&&(m.Reason==CardMoveReasons.Use||m.Reason==CardMoveReasons.Respond||m.Reason==CardMoveReasons.EquipmentUse||m.Reason==CardMoveReasons.Nullification)))
        {
            if(!_cardMovements.Contains(m))throw new InvalidOperationException("Miaoxian cannot freeze a fabricated physical payment.");
            var red=m.From.Zone==CardZoneKind.Hand?original.HandRedCount==1&&original.UniqueRedCardId==m.CardId:original.HandRedCount==0&&original.RedGrainIds.Contains(m.CardId);
            var black=m.From.Zone==CardZoneKind.Hand&&original.HandBlackCount==1&&original.UniqueBlackCardId==m.CardId;if(!red&&!black)continue;
            var actionId=m.Reason==CardMoveReasons.Respond||m.Reason==CardMoveReasons.Nullification?_cardActionSequence+1:batch.ParentFrameId is{}parent&&LifecycleCardUse(parent)?.Action is{}action&&action.PhysicalCards.Any(c=>c.CardId==m.CardId&&c.From==m.From)?action.ActionId:0;
            if(actionId==0)continue;
            AdvanceEventRulesAndQueueFact(new MiaoxianPaymentQualifiedEvent(batch.Id,batch.ParentFrameId,actionId,original.Source,original.GameplayHash,m.CardId,m.CardKind,m.From,m.Sequence,batch.TurnNumber,red,black));
        }
        if(batch.ParentFrameId is not{}nativeParent||ActiveNullificationWindow?.Id!=nativeParent)return;
        foreach(var moved in batch.Movements.Where(m=>m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.NullificationFinished))
        {
            var entries=ProgramEventHistory<MiaoxianPaymentQualifiedEvent>().Where(e=>e.ActionId==_cardActionSequence+1&&e.ParentFrameId==nativeParent&&e.CardId==moved.CardId&&e.Kind==moved.CardKind&&e.ActualTurn==_turnNumber&&e.Sequence<moved.Sequence&&_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Nullification)).Select(e=>e.Sequence).Distinct().ToArray();
            if(entries is[var entry])AdvanceEventRulesAndQueueFact(new MiaoxianNativeCounterspellFinishedEvent(_cardActionSequence+1,nativeParent,moved.CardId,entry,moved.Sequence));
        }
    }
    private bool IsRuanYuMiaoxianBlackAvailable(CharacterState owner,Card card,CardConversionSource source,bool forResponse)
    {
        if(source.SkillId!="ol:miaoxian")return true;
        return HasRuanYuCapability&&source.OwnerSeat==owner.Seat&&owner.IsAlive&&_turnNumber>0&&HasRuntimeSkillInstance(owner,source.SkillId,source.SkillInstanceId)&&ViewAsRule(source)is{}rule&&IsOrdinaryTrick(rule.OutputKind)&&(!forResponse||rule.OutputKind==CardKind.Nullification)&&_cardZones.GetLocation(card.Id)==CardLocation.Hand(owner.Seat)&&GetHand(owner).Where(c=>SuitColor(EffectiveSuit(owner,c))==false).Select(c=>c.Id).ToArray()is[var black]&&black==card.Id&&!ProgramEventHistory<MiaoxianBlackConsumedEvent>().Any(e=>e.Source.OwnerSeat==owner.Seat&&e.ActualTurn==_turnNumber);
    }
    private bool RuanYuActualUse(CardActionContext action)=>action.Type==CardActionType.Use||action.Type==CardActionType.Response&&action.ActorSeat==action.ProviderSeat&&action.RequesterSeat is null&&action.ResponderSeat==action.ActorSeat&&action.EffectiveKind is CardKind.Dodge or CardKind.Nullification;
    private bool RuanYuActualMaterial(MiaoxianPaymentQualifiedEvent paid,CardActionContext action,long parentId)=>paid.ParentFrameId==parentId&&paid.ActionId==action.ActionId&&paid.ActualTurn==_turnNumber&&paid.Source.OwnerSeat==action.ActorSeat&&action.ActorSeat==action.ProviderSeat&&action.PhysicalCards.Any(c=>c.CardId==paid.CardId&&c.CardKind==paid.Kind&&c.From==paid.From)&&_cardMovements.Any(m=>m.Sequence==paid.Sequence&&m.CardId==paid.CardId&&m.CardKind==paid.Kind&&m.From==paid.From&&m.To==(IsProgramAlternativeCost(action,m.CardId)?CardLocation.DrawPile:CardLocation.Processing)&&(m.Reason==CardMoveReasons.Use||m.Reason==CardMoveReasons.Respond||m.Reason==CardMoveReasons.EquipmentUse||m.Reason==CardMoveReasons.Nullification&&action.Type==CardActionType.Response&&action.EffectiveKind==CardKind.Nullification&&ProgramEventHistory<MiaoxianNativeCounterspellFinishedEvent>().Any(e=>e.ActionId==action.ActionId&&e.ParentFrameId==parentId&&e.CardId==paid.CardId&&e.EntrySequence==paid.Sequence&&_cardMovements.Any(f=>f.Sequence==e.FinishSequence&&f.CardId==paid.CardId&&f.CardKind==paid.Kind&&f.From==CardLocation.Processing&&f.To==CardLocation.DiscardPile&&f.Reason==CardMoveReasons.NullificationFinished))));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27430)]private void ObserveRuanYuMiaoxianBlack(IGameEvent payload)
    {
        if(!HasRuanYuCapability)return;CardActionContext? action=null;long parent=0;var declaration=false;
        if(payload is CardUseDeclaredEvent declared&&LifecycleCardUse(declared.ResolutionId)is{Action:{}useAction}use){action=useAction;parent=use.Id;declaration=true;}
        else if(payload is CardActionAcceptedEvent{Action:{Type:CardActionType.Response,EffectiveKind:CardKind.Nullification}response}){action=response;parent=ActiveNullificationWindow?.Id??0;}
        if(action is null||!RuanYuActualUse(action))return;
        foreach(var source in action.ConversionChain.Where(s=>s.SkillId=="ol:miaoxian"))
        {
            if(ProgramEventHistory<MiaoxianBlackConsumedEvent>().Any(e=>e.ActionId==action.ActionId&&e.Source==source))continue;
            if(action.PhysicalCards is not[var cost]||source.OwnerSeat!=action.ActorSeat||action.ActorSeat!=action.ProviderSeat||!IsOrdinaryTrick(action.EffectiveKind)||ViewAsRule(source)?.OutputKind!=action.EffectiveKind)throw new InvalidOperationException("Miaoxian black use changed its original conversion source or ordinary physical use.");
            var valid=declaration?_cardZones.GetLocation(cost.CardId)==CardLocation.Hand(source.OwnerSeat)&&GetHand(_players[source.OwnerSeat]).Where(c=>SuitColor(EffectiveSuit(_players[source.OwnerSeat],c))==false).Select(c=>c.Id).ToArray()is[var only]&&only==cost.CardId:ProgramEventHistory<MiaoxianPaymentQualifiedEvent>().Any(e=>e.Black&&e.Source.SkillInstanceId==source.SkillInstanceId&&e.Source.SkillId==source.SkillId&&RuanYuActualMaterial(e,action,parent));
            if(!valid||ProgramEventHistory<MiaoxianBlackConsumedEvent>().Any(e=>e.Source.OwnerSeat==source.OwnerSeat&&e.ActualTurn==_turnNumber))throw new InvalidOperationException("Miaoxian may consume only the genuine unique black Hand entity once in this actual turn.");
            AdvanceEventRulesAndQueueFact(new MiaoxianBlackConsumedEvent(action.ActionId,source,_turnNumber,cost.CardId));
        }
    }
    private bool HasRuanYuRedPayment(CardActionContext action,CardConversionSource source,string hash,long parentId)=>HasRuanYuCapability&&RuanYuActualUse(action)&&action.ActorSeat==source.OwnerSeat&&action.EffectiveIsRed==true&&ProgramEventHistory<MiaoxianPaymentQualifiedEvent>().Any(e=>e.Red&&e.Source==source&&e.GameplayHash==hash&&RuanYuActualMaterial(e,action,parentId));
    private IReadOnlyList<ProgramCardTriggerCandidate> IncludeRuanYuMiaoxianRedCandidates(IReadOnlyList<ProgramCardTriggerCandidate> ordinary,CardActionContext action,SkillProgramTriggerWindow window)
    {
        if(!HasRuanYuCapability||window!=SkillProgramTriggerWindow.CardUseCommitted||!IsValidPlayerSeat(action.ActorSeat)||!_players[action.ActorSeat].IsAlive||!RuanYuActualUse(action))return ordinary;
        var parent=_resolutionStack.LastOrDefault()?.Id;if(parent is null)return ordinary;var result=ordinary.ToList();
        foreach(var paid in ProgramEventHistory<MiaoxianPaymentQualifiedEvent>().Where(e=>e.Red&&e.ActionId==action.ActionId).DistinctBy(e=>(e.Source,e.GameplayHash)))
        {
            if(!HasRuanYuRedPayment(action,paid.Source,paid.GameplayHash,parent.Value)||_contentRegistry.GetSkill(paid.Source.SkillId).Program is not{}program||program.GameplayHash!=paid.GameplayHash||result.Any(c=>c.OwnerSeat==paid.Source.OwnerSeat&&c.SkillId==paid.Source.SkillId&&c.TriggerId=="unique-red-use"&&c.SkillInstanceId==paid.Source.SkillInstanceId&&c.GameplayHash==paid.GameplayHash))continue;
            var trigger=program.Triggers.Single(t=>t.Id=="unique-red-use");var context=CreateCardActionProgramContext(action,window,0,paid.Source.OwnerSeat,-1,CaptureProgramTriggerFacts(_players[paid.Source.OwnerSeat],action));
            result.Add(new(paid.Source.OwnerSeat,-1,paid.Source.SkillId,trigger.Id,paid.GameplayHash,paid.Source.SkillInstanceId,trigger.Priority,context));
        }
        return result.Count==ordinary.Count?ordinary:Array.AsReadOnly(result.ToArray());
    }
    private bool IsIssuedRuanYuMiaoxianRedCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasRuanYuCapability||c.SkillId!="ol:miaoxian"||c.BindingId!="unique-red-use"||!IsValidPlayerSeat(c.OwnerSeat)||!_players[c.OwnerSeat].IsAlive||context.Window!=SkillProgramTriggerWindow.CardUseCommitted||context.OwnerSeat!=c.OwnerSeat||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]is not{}item||item.OwnerSeat!=c.OwnerSeat||item.SkillId!=c.SkillId||item.TriggerId!=c.BindingId||item.SkillInstanceId!=c.SkillInstanceId||item.GameplayHash!=c.GameplayHash||context.CardUse?.CardActionId!=window.Action.ActionId||!HasRuanYuRedPayment(window.Action,new(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId),c.GameplayHash,window.ParentFrameId))return false;
        return !ProgramEventHistory<MiaoxianRedDrawIssuedEvent>().Any(e=>e.ActionId==window.Action.ActionId&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillInstanceId==c.SkillInstanceId)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.RuanYu?.ActionId==window.Action.ActionId&&MountObserverCandidateMatches(f,c)&&f.WindowContext==context);
    }
}
