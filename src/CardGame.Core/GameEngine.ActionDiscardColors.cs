namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TracksActionDiscardColor=>_contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RewardDiscardedActionColor)==true;
    private bool? CapturePhysicalCardColor(int seat,Card card)=>LiveDeclarationPayment(card.Id) is {} declared ? declared.Cost.EffectiveIsRed : CapturesActionColor?SuitColor(EffectiveSuit(_players[seat],card)):null;
    private bool? CaptureActionColor(IReadOnlyList<CardActionCost> costs,Suit? effectiveSuit=null)
    {
        if(!CapturesActionColor||costs.Count==0)return null;
        if(SuitColor(effectiveSuit) is {} declared)return declared;
        var colors=costs.Select(c=>c.EffectiveIsRed).Distinct().ToArray();return colors.Length==1?colors[0]:null;
    }
    private void CaptureActionDiscardFact(long batchId,int turn,IReadOnlyList<CardMovementRecord> movements)
    {
        if(!TracksActionDiscardColor)return;
        var actual=movements.Where(m=>m.To==CardLocation.DiscardPile&&m.From==CardLocation.Processing && m.Reason.Value is "card.use-finished" or "card.response-finished" or "card.respond.nullification-finished" or "card.effect.iron-chain-finished").ToArray();
        if(actual.Length==0)return;
        var activeUse=_resolutionStack.OfType<CardUseFrame>().LastOrDefault(f=>f.Action is not null);
        var parentAction=activeUse?.Action;
        var response=actual.All(m=>m.Reason==CardMoveReasons.ResponseFinished||m.Reason==CardMoveReasons.NullificationFinished);
        CardActionContext? action;
        if(!response)action=_resolutionStack.OfType<CardUseFrame>().Select(f=>f.Action).LastOrDefault(a=>a is not null&&a.Type==CardActionType.Use&&actual.All(m=>a.PhysicalCards.Any(c=>c.CardId==m.CardId)));
        else
        {
            // Response identity must belong to this active card-use parent. Reused ids in unrelated turns/parents cannot match.
            action=parentAction is null?null:CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).LastOrDefault(a=>a.Type==CardActionType.Response&&a.ParentActionId==parentAction.ActionId&&a.PhysicalCards.Count>0&&actual.All(m=>a.PhysicalCards.Any(c=>c.CardId==m.CardId)));
        }
        if(action is null||action.PhysicalCards.Count==0)return;
        AdvanceEventRulesAndQueueFact(new ActionCardsDiscardedEvent(batchId,action.ActionId,action.ProviderSeat,action.EffectiveIsRed,turn));
    }
}
