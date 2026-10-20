using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool LiveXionghuoSource(XionghuoGrant grant)=>_players[grant.Source.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[grant.Source.OwnerSeat],grant.Source.SkillId,grant.Source.SkillInstanceId)&&_contentRegistry.GetSkill(grant.Source.SkillId).Program?.GameplayHash==grant.GameplayHash;
 [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 10)]
 private int XionghuoActualDamageBonus(IDamageAttempt attack)
 {
  if(attack.IsSourceLess||!IsValidPlayerSeat(attack.SourceSeat)||!IsValidPlayerSeat(attack.TargetSeat)||_players[attack.TargetSeat].XionghuoGrants.Count==0)return 0;
  return XuRongSubjectGrants(attack.SourceSeat,attack.TargetSeat).Any()?1:0;
 }
}
