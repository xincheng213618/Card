namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class HappyZhugeLiangCapability(GameEngine engine):IHappyZhugeLiangHost
    {
        public void GrantMist(ProgramSkillFrame frame)=>engine.GrantHappyDawu(frame);
    }
    private void GrantHappyDawu(ProgramSkillFrame supplied)
    {
        var frame=GetActiveProgramFrame(supplied.Id);
        var bound=GetProgramCardSet(frame,"stars");
        if(bound.CardIds.Count==0)return;
        if(frame.SkillId!=HappyZhugeLiangComposition.Dawu||frame.TriggerId!="self-mist"||frame.InstructionIndex!=3||
            frame.WindowContext is not{Window:SkillProgramTriggerWindow.TurnEnding}||frame.PendingMovementContinuation is not null||
            bound is not{CardIds:[var star],SourceLocations:[var from]}||from!=new CardLocation(CardZoneKind.PrivateReserve,frame.OwnerSeat)||
            ProgramEventHistory<HappyDawuGrantedEvent>().Any(e=>e.FrameId==frame.Id))
            throw new InvalidOperationException("Happy Dawu lost its exact once-paid original Ending instruction and private star source.");
        var history=CompleteProgramEventHistory().ToArray();
        var starts=history.Select((payload,index)=>(payload,index)).Where(item=>item.payload is ProgramBindingStartedEvent start&&
            start.FrameId==frame.Id&&start.SkillId==frame.SkillId&&start.OwnerSeat==frame.OwnerSeat&&start.SkillInstanceId==frame.SkillInstanceId&&
            start.BindingId==frame.TriggerId&&start.Window==SkillProgramTriggerWindow.TurnEnding).ToArray();
        var reason=new CardMoveReason($"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}");
        var costs=starts is[var started]?history.Skip(started.index+1).OfType<CardMovedEvent>().Where(m=>m.CardId==star&&m.From==from&&m.To!=from&&m.Reason==reason).ToArray():[];
        if(costs is not[var cost]||_cardMovements.LastOrDefault(m=>m.CardId==star&&m.From==cost.From&&m.To==cost.To&&m.Reason==reason) is not{}movement)
            throw new InvalidOperationException("Happy Dawu requires the exact physical star movement after this binding actually started.");
        SetProgramBooleanState(frame,HappyZhugeLiangComposition.MistState,true);
        ScheduleProgramBooleanStateExpiration(frame,[HappyZhugeLiangComposition.MistState],SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        AdvanceEventRulesAndQueueFact(new HappyDawuGrantedEvent(frame.Id,CreateProgramTurnEffectSource(frame),frame.GameplayHash,_turnNumber,movement.Sequence));
    }
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers,41480)]
    private IEnumerable<(CardUseEffectSource Source,int Amount)> HappyDawuNormalDamage(IDamageAttempt attack)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantHappyZhugeLiangDawu)||
            GetDamageNature(attack)!=DamageNature.Normal)return [];
        var grant=_programBooleanStateLeases.FirstOrDefault(lease=>lease.Source.OwnerSeat==attack.TargetSeat&&
            lease.Source.SkillId==HappyZhugeLiangComposition.Dawu&&lease.Source.BindingId=="self-mist"&&
            lease.StateId==HappyZhugeLiangComposition.MistState&&lease.Expiration==SkillProgramDamageModifierExpiration.NextOwnerTurnStart&&
            GetProgramBooleanState(lease.Source.OwnerSeat,lease.Source.SkillId,lease.Source.SkillInstanceId,lease.StateId));
        return grant is null?[]:[(grant.Source,-1)];
    }
}
