namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void CompleteZhanyuan(ProgramSkillFrame f,int? male)
 {
  var r=f.HuaMan!;ReplaceRuntimeTop(f=f with{HuaMan=r with{Stage=HuaManStage.GrantChildren,OtherSeat=male},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  if(_players[f.OwnerSeat].IsAlive)
  {
   if(male is{}seat){if(!_players[seat].IsAlive||_players[seat].Gender!=GeneralGender.Male||seat==f.OwnerSeat)throw new InvalidOperationException("Zhanyuan lost the actual chosen male.");var grantSource=$"huaman-zhanyuan:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}";AcquireRuntimeSkills(_players[f.OwnerSeat],grantSource,["ol:xili"]);AcquireRuntimeSkills(_players[seat],grantSource,["ol:xili"]);}
   foreach(var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g=>g.SkillId=="ol:mansi").ToArray())_players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
   AdvanceEventRulesAndQueueFact(new CharacterSkillsLostEvent(f.OwnerSeat,f.OwnerSeat,Array.AsReadOnly(new[]{"ol:mansi"})));
  }
  AdvanceEventRulesAndQueueFact(new ZhanyuanCompletedEvent(f.Id,f.OwnerSeat,male,true));AdvanceRuntimeProgram(f.Id);
 }
}
