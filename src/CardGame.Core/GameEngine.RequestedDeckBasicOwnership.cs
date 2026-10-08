namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardActionType RequestedDeckBasicActionType(RequestedDeckBasicIntent intent) => intent switch
    {
        RequestedDeckBasicIntent.Dying or RequestedDeckBasicIntent.BorrowedSword or RequestedDeckBasicIntent.Qinglong or
            RequestedDeckBasicIntent.ProgramSlash or RequestedDeckBasicIntent.ProgramNearestSlash or RequestedDeckBasicIntent.AssistedSlash or
            RequestedDeckBasicIntent.NearestLegalSlash => CardActionType.Use,
        RequestedDeckBasicIntent.FactionSlash => ActiveFactionCardRequest is { } faction && IsFactionSlashUse(faction) ? CardActionType.Use : CardActionType.Response,
        _ => CardActionType.Response
    };
    private static PendingDecision RequestedDeckBasicNativeDecision(PendingDecision p) => p.Choices.Any(c=>c.Parameters.ContainsKey("deck-basic-source"))
        ? p with {Choices=Array.AsReadOnly(p.Choices.Where(c=>!c.Parameters.ContainsKey("deck-basic-source")).ToArray())} : p;
    // These are invariant projections only. They neither remove a parent nor
    // mutate the live request while the private child is awaiting its answer.
    private PendingDecision? RequestedDeckBasicInvariantDecision()
    {
        if(_resolutionStack.LastOrDefault() is not RequestedDeckBasicFrame view)
            return _pendingDecision is { } p ? RequestedDeckBasicNativeDecision(p) : null;
        AssertRequestedDeckBasicFrame(view);
        return RequestedDeckBasicNativeDecision(view.OriginalDecision);
    }
    private ResolutionFrame? RequestedDeckBasicInvariantTop()
    {
        if(_resolutionStack.LastOrDefault() is not RequestedDeckBasicFrame view)return _resolutionStack.LastOrDefault();
        AssertRequestedDeckBasicFrame(view);
        return _resolutionStack.Single(f=>f.Id==view.ParentFrameId);
    }
    private bool HasValidRequestedDeckBasicMaterial(ResolutionFrame owner)
    {
        if(owner.RequestedDeckBasicMaterial is not { } r || r.OwnerFrameId!=owner.Id || r.ViewFrameId<=r.RequestFrameId ||
            r.ActorSeat<0 || r.ActorSeat>=_players.Count || r.Cursor<0 || r.ViewedCount<0 || r.ViewedCount>4 ||
            r.Source.OwnerSeat!=r.ActorSeat || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            _contentRegistry.Skills.GetValueOrDefault(r.Source.SkillId)?.Program is not { } program || program.GameplayHash!=r.GameplayHash ||
            program.CardPolicies.SingleOrDefault(p=>p.Id==r.Source.BindingId) is not {Kind:SkillProgramCardPolicyKind.PrivateTopBasicRequest,Value:2} ||
            !CompleteProgramEventHistory().OfType<RequestedDeckBasicViewedEvent>().Any(e=>e.FrameId==r.ViewFrameId && e.OwnerFrameId==owner.Id && e.ActorSeat==r.ActorSeat &&
                e.SkillId==r.Source.SkillId && e.Count==r.ViewedCount && e.Intent==r.Intent))return false;
        if(!r.Claiming)return r.SelectedCardId is null && r.SelectedKind is null && r.PaidMovementSequence is null;
        if(r.SelectedCardId is not { } cardId || r.SelectedKind is not { } kind || kind is not(CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol))return false;
        if(r.PaidMovementSequence is not { } sequence)return _cardZones.GetLocation(cardId)==CardLocation.DrawPile &&
            _cardZones.CardsAt(CardLocation.DrawPile).Any(c=>c.Id==cardId && c.Kind==kind);
        return _cardMovements.SingleOrDefault(m=>m.Sequence==sequence) is { } movement && movement.CardId==cardId && movement.CardKind==kind &&
            movement.From==CardLocation.DrawPile && movement.To==CardLocation.Processing &&
            CompleteProgramEventHistory().OfType<RequestedDeckBasicPaidEvent>().Any(e=>e.OwnerFrameId==owner.Id && e.ActorSeat==r.ActorSeat && e.CardId==cardId && e.CardKind==kind && e.SkillId==r.Source.SkillId && e.Intent==r.Intent);
    }
    private void AssertRequestedDeckBasicFrame(RequestedDeckBasicFrame view)
    {
        var r=view.Receipt;
        if(_resolutionStack.LastOrDefault()?.Id!=view.Id || view.Step!=ResolutionFrameStep.AwaitingResponse || r.ViewFrameId!=view.Id ||
            view.ParentFrameId!=r.RequestFrameId || r.Claiming || r.SelectedCardId is not null || r.PaidMovementSequence is not null ||
            view.CardIds.Count!=r.ViewedCount || view.CardIds.Distinct().Count()!=view.CardIds.Count ||
            !_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Take(view.CardIds.Count).Select(c=>c.Id).SequenceEqual(view.CardIds) ||
            view.OriginalDecision.PlayerSeat!=r.ActorSeat || view.OriginalDecision.PromptId!=r.OriginalPromptId || view.OriginalDecision.Revision!=r.OriginalRevision ||
            !_players[r.ActorSeat].IsAlive || r.ActorSeat==_currentSeat ||
            !RequestedDeckBasicSources(_players[r.ActorSeat],RequestedDeckBasicKind(view.OriginalDecision,r.Intent)).Any(p=>p.Source==r.Source) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program!.GameplayHash!=r.GameplayHash ||
            _resolutionStack.SingleOrDefault(f=>f.Id==view.ParentFrameId) is not { } parent ||
            RequestedDeckBasicContext(view.OriginalDecision,parent) is not { } context || context.Intent!=r.Intent ||
            context.OwnerId!=r.OwnerFrameId || context.RequestId!=r.RequestFrameId || context.Cursor!=r.Cursor ||
            _resolutionStack.SingleOrDefault(f=>f.Id==r.OwnerFrameId) is not { } owner || owner.RequestedDeckBasicMaterial?.OriginalPromptId==r.OriginalPromptId ||
            _pendingDecision is not {Kind:DecisionKind.ProgramTrigger,IsPrivate:true} decision || decision.PlayerSeat!=r.ActorSeat ||
            !AssistedChoicesEqual(decision.Choices,RequestedDeckBasicChoices(view)))
            throw new InvalidOperationException("The private basic-card view lost its exact original request, cursor or top order.");
    }
    private bool RequestedDeckBasicFrameRidesOn(ResolutionFrame child,ResolutionFrame parent)
    {
        if(child is not RequestedDeckBasicFrame view || view.ParentFrameId!=parent.Id)return false;
        AssertRequestedDeckBasicFrame(view);return true;
    }
    private void AssertRequestedDeckBasicMaterials()
    {
        foreach(var owner in _resolutionStack.Where(f=>f.RequestedDeckBasicMaterial is not null))
            if(!HasValidRequestedDeckBasicMaterial(owner))throw new InvalidOperationException("A requested DrawPile material lost its owning-view/payment proof.");
        if(_resolutionStack.LastOrDefault() is RequestedDeckBasicFrame view)AssertRequestedDeckBasicFrame(view);
    }
    // The existing DyingResponse/action/observer proof still owns this rescue;
    // this adds only the selected real DrawPile cost provenance to its native branch.
    private bool IsRequestedDeckBasicRescueCost(CardUseFrame use,DyingFrame dying,CardActionCost cost) =>
        _resolutionStack.SingleOrDefault(f=>f.Id==dying.Id) is {RequestedDeckBasicMaterial:{Intent:RequestedDeckBasicIntent.Dying,Claiming:true} r} owner &&
        HasValidRequestedDeckBasicMaterial(owner) && r.OwnerFrameId==dying.Id && r.Cursor==dying.ResponderIndex && r.ActorSeat==use.SourceSeat &&
        r.SelectedCardId==cost.CardId && r.SelectedKind==cost.CardKind && cost.From==CardLocation.DrawPile &&
        cost.CardKind==use.CardKind && r.PaidMovementSequence is not null;
}
