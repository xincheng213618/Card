namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool XiangEquipmentActualActor(XiangChongEquipmentOrigin origin,int finalActor)
    {
        var actor=origin.ActorSeat;
        foreach(var changed in ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==origin.CardUseFrameId))
        {
            if(changed.PreviousActorSeat!=actor||changed.OwnerSeat!=actor||changed.ProviderSeat!=origin.ActorSeat||changed.FrameId<=origin.CardUseFrameId||!IsValidPlayerSeat(changed.ActorSeat)||changed.ActorSeat==actor||
               !ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==changed.FrameId&&e.SkillId==changed.SkillId&&e.OwnerSeat==actor&&e.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized))return false;
            actor=changed.ActorSeat;
        }
        return actor==finalActor;
    }
    private bool ValidXiangEquipmentResult(XiangChongEquipmentResult result)
    {
        var o=result.Origin;
        if(!XiangEquipmentActualActor(o,result.ActorSeat)||ProgramEventHistory<XiangChongEquipmentResultEvent>().Count(e=>e.Result==result)!=1)return false;
        if(result.EntrySequence is not{}sequence)return result.ReplacedCardId is null&&result.Termination is{}terminal&&ValidXiangEquipmentTermination(result,terminal);
        if(result.Termination is not null)return false;
        return _cardMovements.Any(m=>m.Sequence==sequence&&m.CardId==o.CardId&&m.CardKind==o.CardKind&&m.From==CardLocation.Processing&&m.To==CardLocation.Equipment(result.ActorSeat)&&m.Reason==CardMoveReasons.EquipmentEnter)&&
            ProgramEventHistory<EquipmentChangedEvent>().Count(e=>e.ResolutionId==o.CardUseFrameId&&e.PlayerSeat==result.ActorSeat&&e.CardId==o.CardId&&e.CardKind==o.CardKind&&e.ReplacedCardId==result.ReplacedCardId)==1;
    }
    private bool ValidXiangEquipmentTermination(XiangChongEquipmentResult result,XiangChongEquipmentTermination terminal)=>
        terminal.Sequence>0&&_cardMovements.Any(m=>m.Sequence==terminal.Sequence&&m.CardId==result.Origin.CardId&&m.CardKind==result.Origin.CardKind&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason.Value==terminal.Reason)&&
        (terminal.Reason=="area.abolished.equipment"&&!terminal.ActorDead||terminal.Reason==CardMoveReasons.UseFinished.Value&&terminal.ActorDead&&ProgramEventHistory<PlayerDiedEvent>().Any(e=>e.VictimSeat==result.ActorSeat))&&
        ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==result.Origin.CardUseFrameId&&e.CardId==result.Origin.CardId&&e.CardKind==result.Origin.CardKind)==1;
    private void CaptureXiangChongEquipmentResult(CardUseFrame use)
    {
        var o=use.XiangChongGuyingEquipment!;var f=GetActiveProgramFrame(o.ProgramFrameId);
        if(!MatchesXiangChongEquipment(f,o)||!ShownEntityUseActorMatches(use,o.ActorSeat,o.ActorSeat)||f.XiangChong!.NativeResult is not null)
            throw new InvalidOperationException("Guying's final equipment result lost its original issuer or exact actual actor chain.");
        var r=f.XiangChong;var changed=ProgramEventHistory<EquipmentChangedEvent>().SingleOrDefault(e=>e.ResolutionId==use.Id&&e.CardId==o.CardId&&e.CardKind==o.CardKind);
        if(changed is not null&&changed.PlayerSeat!=use.SourceSeat)throw new InvalidOperationException("Guying's native equipment entry belongs to a different actual user.");
        var entry=changed is null?null:_cardMovements.SingleOrDefault(m=>m.Sequence>r.Before&&m.Sequence<=_movementSequence&&m.CardId==o.CardId&&m.From==CardLocation.Processing&&m.To==CardLocation.Equipment(changed.PlayerSeat)&&m.Reason==CardMoveReasons.EquipmentEnter);
        if(changed is not null&&entry is null)throw new InvalidOperationException("Guying's native equipment result lost its actual entry movement.");
        XiangChongCard? replaced=null;
        if(changed?.ReplacedCardId is{}oldId)
        {
            var moved=_cardMovements.SingleOrDefault(m=>m.Sequence>r.Before&&m.Sequence<=_movementSequence&&m.CardId==oldId&&m.From==CardLocation.Equipment(changed.PlayerSeat)&&m.Reason==CardMoveReasons.EquipmentReplace);
            if(moved is null)throw new InvalidOperationException("Guying's native equipment result lost the actual replaced entity.");
            replaced=new(moved.CardId,moved.CardKind,moved.From);
        }
        var grain=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=_movementSequence&&m.From==CardLocation.WoodenOxGrain(use.SourceSeat)&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&m.To==CardLocation.DiscardPile)
            .Select(m=>new XiangChongCard(m.CardId,m.CardKind,m.From)).ToArray();
        XiangChongEquipmentTermination? termination=null;
        if(entry is null)
        {
            var first=_cardMovements.FirstOrDefault(m=>m.Sequence>r.Before&&m.CardId==o.CardId&&m.From==CardLocation.Processing);
            if(first is not{To:var terminalTo}||terminalTo!=CardLocation.DiscardPile||
               !(first.Reason.Value=="area.abolished.equipment"&&_players[use.SourceSeat].EquipmentSlotCapacity(EquipmentCatalog.Get(o.CardKind).Slot)==0||first.Reason==CardMoveReasons.UseFinished&&!_players[use.SourceSeat].IsAlive))
                throw new InvalidOperationException("Guying's equipment without entry requires its exact first native abolished-slot or dead-user terminal discard.");
            termination=new(first.Sequence,first.Reason.Value,first.Reason==CardMoveReasons.UseFinished);
        }
        var result=new XiangChongEquipmentResult(o,use.SourceSeat,entry?.Sequence,changed?.ReplacedCardId){Termination=termination};
        ReplaceRuntimeTop(f with{XiangChong=r with{NativeResult=result,Replaced=replaced,Auxiliaries=grain}});
        AdvanceEventRulesAndQueueFact(new XiangChongEquipmentResultEvent(result));
    }
}
