using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string SayingObtainReason="skill-program.sun-yuan.saying.obtain";
    private bool HasSayingRoundRange(int actor,int target)=>HasSunYuanCapability&&actor!=target&&IsValidPlayerSeat(actor)&&IsValidPlayerSeat(target)&&_players[actor].IsAlive&&_players[target].IsAlive&&ProgramEventHistory<SayingRangeGrantedEvent>().Any(e=>e.Round==_roundNumber&&(e.OwnerSeat==actor&&e.Holder==target||e.Holder==actor&&e.OwnerSeat==target));
    private bool TryGetSayingSlashRequest(ProgramSkillFrame f,out int actorSeat,out IReadOnlyList<int> targetSeats)
    {
        actorSeat=-1;targetSeats=[];
        if(f.SunYuan is not{Paid:true}r||r.Op!=SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply||r.Stage is not(SunYuanStage.Request or SunYuanStage.SlashIssuing or SunYuanStage.NativeUse or SunYuanStage.NativeChildren)||r.Source!=SunYuanSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||ProgramEventHistory<SayingRequestIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.ActorSeat==r.Holder&&e.TargetSeat==f.OwnerSeat)!=1)return false;
        actorSeat=r.Holder;targetSeats=Array.AsReadOnly(new[]{f.OwnerSeat});return true;
    }
    private bool IsSayingSlashRequest(ProgramSkillFrame f)=>f.SunYuan?.Stage==SunYuanStage.Request&&TryGetSayingSlashRequest(f,out _,out _);
    private bool IsSayingSlashQuery(CharacterState actor)
    {
        if(!actor.IsAlive)return false;
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame top)return false;
        if(top.SunYuan?.Stage is SunYuanStage.Request or SunYuanStage.SlashIssuing&&TryGetSayingSlashRequest(top,out var seat,out _)&&seat==actor.Seat&&_players[top.OwnerSeat].IsAlive)return true;
        return top.EquipmentSlotNamedUse is{OriginalParentId:{}parent}&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==parent)is{}owner&&top.OwnerSeat==actor.Seat&&_players[owner.OwnerSeat].IsAlive&&SayingEquipmentSlotPaymentRidesOn(owner,top);
    }


    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 10)]
    private bool DistanceSpecificSlashExemption10HasSayingRoundRange(ProgramSlashDistanceQuery q) =>
        HasSayingRoundRange(q.Actor.Seat, q.Target.Seat);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 40)]
    private bool SayingSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        IsSayingSlashQuery(query.Actor);
}
