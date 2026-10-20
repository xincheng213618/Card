using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsWenYangChoujueCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>IsValidPlayerSeat(candidate.OwnerSeat)&&_players[candidate.OwnerSeat].IsAlive&&WenYangChoujueParent(candidate,c);
    private bool WenYangChoujueParent(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>candidate.SkillId=="ol:choujue"&&candidate.BindingId is("own-actual-turn-ended"or"foreign-actual-turn-ended")&&
        c.Window==SkillProgramTriggerWindow.AfterTurnEnded&&c.OwnerSeat==candidate.OwnerSeat&&IsValidPlayerSeat(candidate.OwnerSeat)&&
        _contentRegistry.GetSkill(candidate.SkillId).Program is{}program&&program.GameplayHash==candidate.GameplayHash&&GetProgramTrigger(candidate).Effects is[{Op:SkillProgramEffectOp.ResolveWenYangAwakening}]&&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==c.ParentFrameId)is{}parent&&IsActualAfterTurnEndedParent(parent)&&parent.AfterTurnEnded is{}window&&window.ItemIndex>=0&&window.ItemIndex<window.Items.Count&&window.Items[window.ItemIndex].Candidate==candidate&&
        c.SourceSeat==parent.OwnerSeat&&c.TargetSeat==parent.OwnerSeat&&c.OccurrenceIndex==candidate.OccurrenceIndex&&c.Facts==window.Items[window.ItemIndex].Facts&&
        (candidate.BindingId=="own-actual-turn-ended"?candidate.OwnerSeat==parent.OwnerSeat:candidate.OwnerSeat!=parent.OwnerSeat);
}
