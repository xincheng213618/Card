namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsProvenanceOwnedZone(CardLocation location,int owner) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProvenanceCardPolicies.IsProvenanceOwnedZone(location,owner);
    partial void TryFreezeProvenanceCounterspellSource(CharacterState owner,IReadOnlyList<int> physicalCardIds,ref CardConversionSource? source) => source=FreezeProvenanceCounterspellSource(owner,physicalCardIds);
    private CardConversionSource? FreezeProvenanceCounterspellSource(CharacterState owner,IReadOnlyList<int> ids)=>FreezeProvenanceCardSource(owner,ids);
    private bool HasProvenanceCounterspellMaterials(CharacterState owner,IReadOnlyList<int> validIds)=>validIds.Any(id=>FreezeProvenanceCardSource(owner,[id]) is not null);
    private bool HasProvenanceGrainCounterspellMaterials(CharacterState owner,IReadOnlyList<int> validIds)=>validIds.Any(id=>_cardZones.GetLocation(id)==CardLocation.WoodenOxGrain(owner.Seat) && FreezeProvenanceCardSource(owner,[id]) is not null);
    private CardConversionSource? FreezeProvenanceCardSource(CharacterState owner,IReadOnlyList<int> ids)
    {
        if(!TracksDiscardedEntityProvenance || !owner.IsAlive || !owner.IsFaceDown || ids.Count==0 || ids.Distinct().Count()!=ids.Count) return null;
        foreach(var policy in CardPolicies(owner,SkillProgramCardPolicyKind.ClaimedEntitiesFaceDownUse))
        {
            if(ids.All(id=>IsProvenanceEntityCurrentlyHeld(owner,id,policy.Policy.Id)))
                return new(policy.Source.SkillId,policy.Policy.Id,owner.Seat,policy.Source.SkillInstanceId);
        }
        return null;
    }
    private bool IsProvenanceEntityCurrentlyHeld(CharacterState owner,int id,string tag)
    {
        var claimed=CompleteProgramEventHistory().OfType<ProgramDiscardedEntityClaimedEvent>().LastOrDefault(e=>e.CardId==id);
        if(claimed is null || claimed.Origin.OwnerSeat!=owner.Seat || claimed.ProvenanceId!=tag ||
            !HasRuntimeSkillInstance(owner,claimed.Origin.SkillId,claimed.Origin.SkillInstanceId) ||
            !_cardMovements.Any(m=>m.Sequence==claimed.ClaimMovementSequence && m.CardId==id && m.From==CardLocation.DiscardPile && m.To==CardLocation.Hand(owner.Seat))) return false;
        var outside=_cardMovements.Where(m=>m.CardId==id && m.Sequence>claimed.ClaimMovementSequence && !IsProvenanceOwnedZone(m.To,owner.Seat)).ToArray();
        if(IsProvenanceOwnedZone(_cardZones.GetLocation(id),owner.Seat)) return outside.Length==0;
        // The only admitted Processing entity is the one exact successful,
        // unclaimed declaration cost already owned by its typed producer.
        if(UnclaimedDeclarationPayment(owner.Seat,id) is not { } payment || payment.ActorSeat!=owner.Seat ||
            payment.Cost.From!=CardLocation.Hand(owner.Seat) || payment.Source.OwnerSeat!=owner.Seat ||
            ViewAsRule(payment.Source)?.DeclarationValidation is null || outside is not [var entry] ||
            entry.CardKind!=payment.Cost.CardKind || entry.From!=payment.Cost.From || entry.To!=CardLocation.Processing ||
            entry.Reason.Value!="conversion.declaration.pay" || _cardMovements.Last(m=>m.CardId==id).Sequence!=entry.Sequence ||
            !CompleteProgramEventHistory().OfType<CardDeclarationCommittedEvent>().Any(e=>e.DeclarationId==payment.DeclarationId && e.OwnerSeat==owner.Seat && e.ActorSeat==owner.Seat && e.DeclaredKind==payment.DeclaredKind)) return false;
        return _resolutionStack.Any(frame=>frame.Id==payment.OwnerFrameId &&
            (frame.AcceptedDeclarationPayment==payment || frame is CardDeclarationFrame {Stage:CardDeclarationStage.Applying,Succeeded:true} declaration &&
                declaration.Id==payment.DeclarationId && declaration.OwnerSeat==owner.Seat && declaration.Return.ActorSeat==owner.Seat && declaration.Payment==payment));
    }
    private bool HasProvenanceUseDistance(CharacterState owner,IReadOnlyList<int>? physicalIds)=>physicalIds is {Count:>0} && FreezeProvenanceCardSource(owner,physicalIds) is not null;
    private bool HasPotentialProvenanceSlash(CharacterState owner)=>TracksDiscardedEntityProvenance && owner.IsFaceDown && (GetSlashUseCards(owner).Any(c=>HasProvenanceUseDistance(owner,c)) || GetZhangbaSlashPairs(owner).Any(pair=>HasProvenanceUseDistance(owner,pair.Select(c=>c.Id).ToArray())) || GetProgramMultiCardViewAsSelections(owner,CardKind.Slash,false).Any(s=>HasProvenanceUseDistance(owner,s.Cards.Select(c=>c.Id).ToArray())));
    private bool HasProvenanceUseDistance(CharacterState owner,Card card)=>card.Id>0 && HasProvenanceUseDistance(owner,new[]{card.Id});
    private bool HasIssuedProvenanceUseDistance(long? useId,int owner)=>useId is { } id && LifecycleCardUse(id) is { ProvenanceUseSource:{ } source, Action:{ } action } && source.OwnerSeat==owner && action.ActorSeat==owner && action.ProviderSeat==owner;
    private void ClearProvenanceUseOnActorChange(long useId,int actor)
    {
        var use=LifecycleCardUse(useId);
        if(use?.ProvenanceUseSource is not { } source || source.OwnerSeat==actor) return;
        UpdateLifecycleCardUse(useId,f=>f with { ProvenanceUseSource=null, IssuedNoResponse=f.IssuedNoResponse?.Source==source?null:f.IssuedNoResponse });
    }
    private void IssueProvenanceUsePolicy(long useId,CardActionContext? action)
    {
        if(action is not { Type:CardActionType.Use } || action.ActorSeat!=action.ProviderSeat || action.PhysicalCards.Count==0 ||
            action.PhysicalCards.Any(c=>!IsProvenanceOwnedZone(c.From,action.ActorSeat))) return;
        var source=FreezeProvenanceCardSource(_players[action.ActorSeat],action.PhysicalCards.Select(c=>c.CardId).ToArray());
        if(source is null) return;
        var issued=new IssuedCardNoResponse(action.ActionId,useId,source,_turnNumber,_cardUseDebitPhaseInstanceId);
        UpdateLifecycleCardUse(useId,f=>f with { IssuedNoResponse=issued,ProvenanceUseSource=source });
        AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(issued));
    }

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 150)]
    private bool DistancePhysicalSlash150HasIssuedProvenanceUseDistance(ProgramSlashDistanceQuery q) =>
        (HasIssuedProvenanceUseDistance(q.ExistingUseFrameId, q.Actor.Seat) || HasIssuedGrantedPhaseEntityDistance(q.ExistingUseFrameId, q.Actor.Seat));


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 160)]
    private bool DistancePhysicalSlash160HasProvenanceUseDistance(ProgramSlashDistanceQuery q) =>
        (HasProvenanceUseDistance(q.Actor, q.PhysicalCardIds ?? (q.PhysicalCard!.Id > 0 ? new[] { q.PhysicalCard!.Id } : [])) || HasGrantedPhaseEntityDistance(q.Actor, q.PhysicalCardIds ?? (q.PhysicalCard!.Id > 0 ? new[] { q.PhysicalCard!.Id } : [])));


    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 60)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 80)]
    private bool DistanceVirtualSlash60HasProvenanceUseDistance(ProgramSlashDistanceQuery q) =>
        (HasProvenanceUseDistance(q.Actor, q.PhysicalCardIds) || HasGrantedPhaseEntityDistance(q.Actor, q.PhysicalCardIds));




    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 90)]
    private bool DistanceBorrowedSlash90HasPotentialProvenanceSlash(ProgramSlashDistanceQuery q) =>
        q.AllowAnyPhysicalSuit && (HasPotentialProvenanceSlash(q.Actor) || HasPotentialGrantedPhaseSlash(q.Actor));


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 50)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 40)]
    private bool DistanceBorrowedPhysicalSlash50HasProvenanceUseDistance(ProgramSlashDistanceQuery q) =>
        (HasProvenanceUseDistance(q.Actor,q.PhysicalCard!) || HasGrantedPhaseEntityDistance(q.Actor,[q.PhysicalCard!.Id]));



}
