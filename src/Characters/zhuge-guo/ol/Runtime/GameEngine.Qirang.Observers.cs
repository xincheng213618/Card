namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 90)]
    private void CaptureQirangDeclaredUse(IGameEvent payload)
    {
        if(!_hasIssuedZhugeGuoState&&!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse))return;
        if(payload is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId)is not{Action:{Type:CardActionType.Use}a}use)return;
        if(EquipmentCatalog.IsEquipment(a.EffectiveKind) && a.PhysicalCards is[var material] && _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse) && CollectProgramTriggerCandidates(_players[a.ActorSeat],SkillProgramTriggerWindow.CardUseTargetsFinalized).Any(c=>ProgramInstructionResolver.Default.Features(GetProgramTrigger(c)).HasOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse)))
        {
            var origin=new QirangEquipmentOrigin(use.Id,a,_movementSequence);ReplaceRuntimeFrame(use.Id,use=use with{QirangEquipmentOrigin=origin});AdvanceEventRulesAndQueueFact(new QirangEquipmentOriginCapturedEvent(use.Id,a.ActionId,a.ActorSeat,material.CardId,origin.MovementBefore));
        }
        if(!_hasIssuedZhugeGuoState || !IsOrdinaryTrick(a.EffectiveKind) || a.ActorSeat!=a.ProviderSeat || a.PhysicalCards is not[var cost] || cost.From!=CardLocation.Hand(a.ActorSeat) || _cardZones.GetLocation(cost.CardId)!=cost.From || _players[a.ActorSeat].QirangEntityLeases is not{Count:>0}leases)return;
        var lease=leases.FirstOrDefault(l=>l.CardId==cost.CardId&&l.PrintedKind==a.EffectiveKind&&l.OwnerSeat==a.ActorSeat&&HasRuntimeSkillInstance(_players[a.ActorSeat],l.Source.SkillId,l.Source.SkillInstanceId));if(lease is null)return;
        var q=new QirangUseQualification(lease,use.Id,a,_movementSequence);ReplaceRuntimeFrame(use.Id,use with{QirangQualification=q});AdvanceEventRulesAndQueueFact(new QirangUseQualifiedEvent(use.Id,a.ActionId,a.ActorSeat,cost.CardId,lease.GainSequence,q.MovementBefore));
    }
    private bool ValidQirangQualification(QirangUseQualification q)
    {
        var l=q.Lease;var a=q.OriginalAction;
        return l.Source.OwnerSeat==l.OwnerSeat&&a.ActorSeat==l.OwnerSeat&&a.ProviderSeat==l.OwnerSeat&&a.Type==CardActionType.Use&&a.PhysicalCards is[var physical]&&physical.CardId==l.CardId&&physical.From==CardLocation.Hand(l.OwnerSeat)&&physical.CardKind==l.PrintedKind&&a.EffectiveKind==l.PrintedKind&&IsOrdinaryTrick(l.PrintedKind)&&l.GainSequence>0&&q.MovementBefore>=l.GainSequence&&_contentRegistry.GetSkill(l.Source.SkillId).Program?.GameplayHash==l.GameplayHash&&
            CompleteProgramEventHistory().OfType<QirangEntityLeaseIssuedEvent>().Count(e=>e.Lease==l)==1&&CompleteProgramEventHistory().OfType<QirangUseQualifiedEvent>().Count(e=>e==new QirangUseQualifiedEvent(q.CardUseFrameId,a.ActionId,l.OwnerSeat,l.CardId,l.GainSequence,q.MovementBefore))==1&&
            _cardMovements.Count(m=>m.CardId==l.CardId&&m.Sequence==l.GainSequence&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(l.OwnerSeat)&&m.Reason.Value==QirangGainReason)==1&&
            _cardMovements.Count(m=>m.CardId==l.CardId&&m.Sequence>q.MovementBefore&&m.From==physical.From&&m.To==CardLocation.Processing&&m.Reason==(a.EffectiveKind==CardKind.IronChain?CardMoveReasons.IronChainUse:CardMoveReasons.Use))==1;
    }
}
