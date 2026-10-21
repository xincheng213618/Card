namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MouJxHandDistanceQuery(CharacterState actor,IReadOnlyList<int>? physical)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouJiaXu:{Yin:true,Stage:MouJiaXuStage.Use or MouJiaXuStage.IssuingUse}r}f||r.Holder!=actor.Seat||!actor.IsAlive||!MouJiaXu.Valid(f))return false;
  if(physical is{Count:>0})return physical is[var id]&&IsMouJxExactCardQuery(actor,id);
  return r.Stage==MouJiaXuStage.IssuingUse?r.Native is{UseId:0,ActionId:0}n&&IsMouJxExactCardQuery(actor,n.CardId):r.Remaining.Any(id=>IsMouJxExactCardQuery(actor,id));
 }
 private bool MouJxBoundDistance(long id,int actor)=>LifecycleCardUse(id)is{MouJiaXuFumou:{}n}u&&n.Actor==actor&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId)is{}parent&&MouJxNativeRidesOn(u,parent);
 [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption,36500)]private bool MouJxCardDistance(ProgramCardDistanceQuery q)=>q.CardUseFrameId is{}id?MouJxBoundDistance(id,q.Actor.Seat):MouJxHandDistanceQuery(q.Actor,q.PhysicalCardIds);
 [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash,36500)][ProgramDistanceRule(ProgramDistanceStage.VirtualSlash,36500)][ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption,36500)]private bool MouJxSlashDistance(ProgramSlashDistanceQuery q)=>q.ExistingUseFrameId is{}id?MouJxBoundDistance(id,q.Actor.Seat):MouJxHandDistanceQuery(q.Actor,q.PhysicalCardIds??(q.PhysicalCard is{}card?[card.Id]:null));
}
