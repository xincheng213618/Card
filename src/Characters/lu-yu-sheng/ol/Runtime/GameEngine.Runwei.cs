using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueLuYuShengRunwei(ProgramSkillFrame f,int delta)
    {
        var r=f.LuYuSheng!;if(r.Phase is not{Kind:ActualDiscardRecoveryPhaseKind.Discard}p||p.ActorSeat!=r.Payer||CurrentActualDiscardRecoveryPhase()!=p||!IsRecordedActualDiscardRecoveryPhase(p)||
            r.Operation!=SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase||r.Branch!=(delta<0?"draw":"discard")||ProgramEventHistory<LuYuShengRunweiPhaseIssuedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Runwei requires its exact accepted real Discard phase and one committed direction.");
        AdvanceEventRulesAndQueueFact(new LuYuShengRunweiPhaseIssuedEvent(f.Id,r.Source,p,delta));
    }
    private int LuYuShengRunweiHandLimitBonus(int actorSeat)=>_phase==TurnPhase.Discard&&CurrentActualDiscardRecoveryPhase()is{Kind:ActualDiscardRecoveryPhaseKind.Discard}p&&p.ActorSeat==actorSeat?
        ProgramEventHistory<LuYuShengRunweiPhaseIssuedEvent>().Where(e=>e.Phase==p).Sum(e=>e.Delta):0;
}
