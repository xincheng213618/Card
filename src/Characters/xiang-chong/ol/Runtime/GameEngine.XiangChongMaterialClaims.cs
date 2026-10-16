namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The original material is already in Processing. A nested real equipment
    // use claims that exact payment without fabricating a same-zone movement.
    private void RecordXiangChongEquipmentMaterialClaim(ProgramSkillFrame frame,XiangChongEquipmentOrigin origin)
    {
        if(origin.From!=CardLocation.Processing)return;
        var f=_resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==frame.Id);
        if(!MatchesXiangChongEquipment(f,origin)||f.XiangChong is not{Lost:{}lost,MaterialClaim:null}r||
           f.WindowContext?.MovementBatch is not{ParentFrameId:{}originalId}batch||originalId==origin.CardUseFrameId||
           LifecycleCardUse(originalId)is not{Action:{Type:CardActionType.Use}action}||
           !action.PhysicalCards.Any(c=>c.CardId==origin.CardId&&c.CardKind==origin.CardKind&&c.From==lost.From)||
           _cardZones.GetLocation(origin.CardId)!=CardLocation.Processing||
           _cardMovements.LastOrDefault(m=>m.CardId==origin.CardId)?.Sequence!=r.LossSequence||
           !batch.Movements.Any(m=>m.Sequence==r.LossSequence&&m.CardId==origin.CardId&&m.CardKind==origin.CardKind&&m.From==lost.From&&m.To==CardLocation.Processing)||
           LifecycleCardUse(origin.CardUseFrameId)is not{XiangChongGuyingEquipment:{}issued}||issued!=origin)
            throw new InvalidOperationException("Guying's nested equipment must claim its exact original live use material before any movement.");
        var claim=new XiangChongEquipmentMaterialClaim(originalId,action.ActionId,batch.Id,r.LossSequence,lost.From,r.Source,r.GameplayHash,origin);
        ReplaceRuntimeFrame(f.Id,f with{XiangChong=r with{MaterialClaim=claim}});
        AdvanceEventRulesAndQueueFact(new XiangChongEquipmentMaterialClaimIssuedEvent(claim));
    }
    private bool ValidXiangEquipmentMaterialInvoice(XiangChongEquipmentMaterialClaim c)=>
        c.Equipment.From==CardLocation.Processing&&c.OriginalUseId!=c.Equipment.CardUseFrameId&&c.LossSequence>0&&c.LossBatchId>0&&
        c.Source.SkillId=="ol:guying"&&c.Source.OwnerSeat==c.Equipment.ActorSeat&&
        _contentRegistry.GetSkill(c.Source.SkillId).Program?.GameplayHash==c.GameplayHash&&
        ProgramEventHistory<XiangChongEquipmentMaterialClaimIssuedEvent>().Count(e=>e.Claim==c)==1&&
        ProgramEventHistory<XiangChongGuyingInvokedEvent>().Count(e=>e.FrameId==c.Equipment.ProgramFrameId&&e.OwnerSeat==c.Equipment.ActorSeat&&e.Source==c.Source&&e.BatchId==c.LossBatchId&&e.LossSequence==c.LossSequence&&e.CardId==c.Equipment.CardId)==1&&
        ProgramEventHistory<XiangChongEquipmentIssuedEvent>().Count(e=>e.Origin==c.Equipment)==1&&
        _cardMovements.Any(m=>m.Sequence==c.LossSequence&&m.CardId==c.Equipment.CardId&&m.CardKind==c.Equipment.CardKind&&m.From==c.OriginalFrom&&m.To==CardLocation.Processing);
    private bool IsXiangChongEquipmentMaterialClaim(long useId,int cardId,CardActionContext action)
    {
        foreach(var e in ProgramEventHistory<XiangChongEquipmentMaterialClaimIssuedEvent>())
        {
            var c=e.Claim;var o=c.Equipment;
            if(c.OriginalUseId!=useId||c.OriginalActionId!=action.ActionId||o.CardId!=cardId||!ValidXiangEquipmentMaterialInvoice(c)||
               !action.PhysicalCards.Any(p=>p.CardId==cardId&&p.CardKind==o.CardKind&&p.From==c.OriginalFrom))continue;
            if(LifecycleCardUse(o.CardUseFrameId)is{XiangChongGuyingEquipment:{}native,Action:{Type:CardActionType.Use}a}inner&&native==o&&
               a.PhysicalCards is[var cost]&&cost.CardId==cardId&&cost.CardKind==o.CardKind&&cost.From==CardLocation.Processing&&
               ShownEntityUseActorMatches(inner,o.ActorSeat,o.ActorSeat)&&
               _resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.Id==o.ProgramFrameId&&f.XiangChong?.MaterialClaim==c)&&XiangEquipmentMaterialLiveLocation(c,inner))return true;
            if(ProgramEventHistory<XiangChongEquipmentMaterialEnteredEvent>().Any(x=>x.Claim==c&&
               _cardMovements.FirstOrDefault(candidate=>candidate.CardId==cardId&&candidate.Sequence>c.LossSequence)is{}m&&m.Sequence==x.Sequence&&m.CardKind==o.CardKind&&m.From==CardLocation.Processing&&m.To==CardLocation.Equipment(x.EquippedSeat)&&m.Reason==CardMoveReasons.EquipmentEnter&&
               ProgramEventHistory<EquipmentChangedEvent>().Any(ch=>ch.ResolutionId==o.CardUseFrameId&&ch.PlayerSeat==x.EquippedSeat&&ch.CardId==cardId&&ch.CardKind==o.CardKind))&&
               ProgramEventHistory<XiangChongEquipmentReturnedEvent>().Count(x=>x.Origin==o&&x.Issued)==1)return true;
            if(ProgramEventHistory<XiangChongEquipmentResultEvent>().Any(x=>x.Result.Origin==o&&x.Result.EntrySequence is null&&x.Result.Termination is{}terminal&&ValidXiangEquipmentResult(x.Result)&&
               _cardMovements.FirstOrDefault(m=>m.CardId==cardId&&m.Sequence>c.LossSequence)?.Sequence==terminal.Sequence)&&
               ProgramEventHistory<XiangChongEquipmentReturnedEvent>().Count(x=>x.Origin==o&&x.Issued)==1)return true;
        }
        return false;
    }
    private bool XiangEquipmentMaterialLiveLocation(XiangChongEquipmentMaterialClaim claim,CardUseFrame inner)
    {
        var o=claim.Equipment;var first=_cardMovements.FirstOrDefault(m=>m.CardId==o.CardId&&m.Sequence>claim.LossSequence);
        if(first is null)return _cardZones.GetLocation(o.CardId)==CardLocation.Processing;
        if(first.From!=CardLocation.Processing)return false;
        if(first.To==CardLocation.Equipment(inner.SourceSeat)&&first.Reason==CardMoveReasons.EquipmentEnter)
            return ProgramEventHistory<EquipmentChangedEvent>().Any(e=>e.ResolutionId==inner.Id&&e.PlayerSeat==inner.SourceSeat&&e.CardId==o.CardId&&e.CardKind==o.CardKind);
        return first.To==CardLocation.DiscardPile&&(first.Reason.Value=="area.abolished.equipment"&&_players[inner.SourceSeat].EquipmentSlotCapacity(EquipmentCatalog.Get(o.CardKind).Slot)==0||first.Reason==CardMoveReasons.UseFinished&&!_players[inner.SourceSeat].IsAlive);
    }
    private void CaptureXiangChongEquipmentMaterialEntry(ProgramSkillFrame f)
    {
        if(f.XiangChong?.MaterialClaim is not{}c)return;
        if(!ValidXiangEquipmentMaterialInvoice(c))throw new InvalidOperationException("Guying's completed nested equipment lost its original material invoice.");
        var o=c.Equipment;var moved=_cardMovements.FirstOrDefault(m=>m.CardId==o.CardId&&m.Sequence>c.LossSequence);
        if(moved is not{To:{Zone:CardZoneKind.Equipment,OwnerSeat:{}seat}}||moved.From!=CardLocation.Processing||moved.Reason!=CardMoveReasons.EquipmentEnter||
           !ProgramEventHistory<EquipmentChangedEvent>().Any(e=>e.ResolutionId==o.CardUseFrameId&&e.PlayerSeat==seat&&e.CardId==o.CardId&&e.CardKind==o.CardKind))return;
        if(ProgramEventHistory<XiangChongEquipmentMaterialEnteredEvent>().Any(e=>e.Claim==c))throw new InvalidOperationException("Guying cannot complete the same original equipment claim twice.");
        AdvanceEventRulesAndQueueFact(new XiangChongEquipmentMaterialEnteredEvent(c,moved.Sequence,seat));
    }
}
