using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsBuZhiActivation(SkillProgramActivation a)=>
        ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.ResolveRebelCountEquipmentChoice);
    private int? BuZhiPhaseLimit(CharacterState owner,SkillProgramActivation a)=>IsBuZhiActivation(a)?_players.Count(p=>p.IsAlive && p.Role==Role.Rebel):a.UsesPerPhase;
    private bool BuZhiActivationTargetAllowed(CharacterState target,SkillProgramActivation a)=>!IsBuZhiActivation(a) || target.IsAlive && GetEquipment(target).Count>0;
    private bool BuZhiPaidEquipmentEntitlement(ProgramSkillFrame f)=>f.BuZhi is{BatchId:null,Target:{}target} &&
        IsValidPlayerSeat(target) && _players[target].IsAlive && ValidBuZhi(f) &&
        CompleteProgramEventHistory().OfType<BuZhiMovementPaidEvent>().Count(e=>e.FrameId==f.Id && e.Target==target && e.ReturnStage=="branch" && e.CardIds.Count==0)==1;
    private void PayBuZhiEquipment(ProgramSkillFrame f,IReadOnlyList<int> ids,CardLocation destination,string next)
    {
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{Stage="movement",ReturnStage=next,Before=before,After=before,Actual=0,PaidIds=ids},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var logicalDiscard=destination==CardLocation.DiscardPile?CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,f.OwnerSeat,ids,new(BuZhiMoveReason),f.Id):null;
        MoveProgramCardsFromMultipleSources(ids,destination,new(BuZhiMoveReason),(_,records)=>
        {
            var active=GetActiveProgramFrame(f.Id);var actual=records.Count(m=>m.To==destination || destination==CardLocation.DiscardPile && m.To==CardLocation.OutsideGame);
            ReplaceRuntimeTop(active with{BuZhi=active.BuZhi! with{After=_movementSequence,Actual=actual}});
            AdvanceEventRulesAndQueueFact(new BuZhiMovementPaidEvent(f.Id,next,active.BuZhi!.Target!.Value,before,_movementSequence,actual,ids));
        });
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
    }
    private void IssueBuZhiDamage(ProgramSkillFrame f)
    {
        if(!BuZhiCurrent(f) || !_players[f.BuZhi!.Target!.Value].IsAlive){FinishBuZhi(f);return;}
        ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{Stage="damage-issued",DamageIssued=true}});
        AdvanceEventRulesAndQueueFact(new BuZhiDamageIssuedEvent(f.Id,f.OwnerSeat,f.BuZhi!.Target!.Value,1));BeginProgramSkillDamage(f,f.BuZhi.Target.Value,1);
    }
}
