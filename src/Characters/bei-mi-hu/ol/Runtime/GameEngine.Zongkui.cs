using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int[] BeiMiHuUnmarkedOthers(int owner)=>_players.Where(p=>p.IsAlive && p.Seat!=owner && p.Markers.GetValueOrDefault(PlayerMarkerKind.Kui)==0).Select(p=>p.Seat).ToArray();
    private int[] BeiMiHuLowestUnmarkedOthers(int owner)
    {
        var eligible=BeiMiHuUnmarkedOthers(owner).Where(s=>_players[s].HasHpValue).ToArray();if(eligible.Length==0)return [];
        var hp=eligible.Min(s=>_players[s].Hp);return eligible.Where(s=>_players[s].Hp==hp).ToArray();
    }
    private SkillProgramStepOutcome BeginBeiMiHuMark(ProgramSkillFrame f,SkillProgramEffectOp op)
    {
        if(!ExactBeiMiHuLifecycleParent(f) || !CanOfferBeiMiHuLifecycle(f.OwnerSeat,GetProgramTrigger(f),f.WindowContext!))throw new InvalidOperationException("Zongkui lost its actual own prelude or first-round boundary.");
        var kind=op==SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart?"round":"before-turn";
        var candidates=kind=="round"?BeiMiHuLowestUnmarkedOthers(f.OwnerSeat):BeiMiHuUnmarkedOthers(f.OwnerSeat);
        var r=NewBeiMiHuReceipt(f,kind+"-choice") with{Candidates=candidates};ReplaceRuntimeTop(f=f with{BeiMiHu=r});
        AdvanceEventRulesAndQueueFact(new BeiMiHuChoiceStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Round,kind,r.Candidates));PublishBeiMiHuChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
}
