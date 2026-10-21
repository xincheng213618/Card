namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MouGjJudgmentRequest(ProgramSkillFrame f,int target,string reason,string bind,SkillProgramCardSetVisibility visibility)=>MouGuoJia.Valid(f)&&f.MouGuoJia is{Stage:MouGuoJiaStage.Judging,Yin:true,Operation:SkillProgramEffectOp.ResolveMouGuoJiaXianmou}r&&r.Actual>=r.X&&r.X>0&&target==f.OwnerSeat&&reason==JudgmentReasons.Lightning&&bind=="mou-xianmou-lightning"&&visibility==SkillProgramCardSetVisibility.Public;
 private void BeginMouGjJudgment(ProgramSkillFrame f)
 {
  if(!MouGjJudgmentRequest(f,f.OwnerSeat,JudgmentReasons.Lightning,"mou-xianmou-lightning",SkillProgramCardSetVisibility.Public)||f.MouGuoJia!.JudgmentId!=0)throw new InvalidOperationException("Xianmou can issue only one native Lightning judgment after its real X-card discard.");
  var id=_resolutionSequence+1;ReplaceRuntimeFrame(f.Id,f=f with{MouGuoJia=f.MouGuoJia with{JudgmentId=id}});_=BeginJudgment(null,f.OwnerSeat,JudgmentReasons.Lightning,f.Id,CardKind.Lightning,JudgmentContinuationKind.ProgramSkill,sourceSeat:f.OwnerSeat,programResultBind:"mou-xianmou-lightning",programResultVisibility:SkillProgramCardSetVisibility.Public);
 }
 private bool IsMouGjJudgment(JudgmentFrame j,ProgramSkillFrame f)=>MouGjJudgmentRequest(f,j.TargetSeat,j.Reason,j.ProgramResultBind??"",j.ProgramResultVisibility??SkillProgramCardSetVisibility.Private)&&f.MouGuoJia!.JudgmentId==j.Id&&j.ParentFrameId==f.Id&&j.SourceSeat==f.OwnerSeat&&j.Continuation==JudgmentContinuationKind.ProgramSkill;
 private bool IsMouGjLightningJudgment(JudgmentFrame j)=>_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==j.ParentFrameId)is{}f&&IsMouGjJudgment(j,f);
 private bool ReturnMouGjJudgment(JudgmentFrame j)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouGuoJia:{Stage:MouGuoJiaStage.Judging}}f||j.ParentFrameId!=f.Id)return false;
  if(!IsMouGjJudgment(j,f)||_resolutionStack.Any(x=>x.Id==j.Id))throw new InvalidOperationException("Xianmou received a different or still-active native judgment return.");
  var result=ProgramEventHistory<JudgmentResolvedEvent>().Single(e=>e.ResolutionId==j.Id&&e.ParentResolutionId==f.Id&&e.TargetSeat==f.OwnerSeat&&e.Reason==JudgmentReasons.Lightning);CardMovementRecord? handoff=null;
  if(result.CardId is{}id&&_cardZones.GetLocation(id)==CardLocation.Processing)
  {
   handoff=_cardMovements.LastOrDefault(m=>m.CardId==id);if(handoff is null||handoff.From!=CardLocation.Judgment(f.OwnerSeat)||handoff.To!=CardLocation.Processing||handoff.Reason.Value!="skill-program.judgment.result")throw new InvalidOperationException("Xianmou cannot retire a claimed or unrelated judgment entity.");
  }
  MouGuoJia.JudgmentReturned(f,result,handoff?.Sequence);f=GetActiveProgramFrame(f.Id);
  if(handoff is not null)MoveCard(GetAdvancedCard(handoff.CardId),CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.JudgmentFinish,beforeFact:m=>{var own=GetActiveProgramFrame(f.Id);ReplaceRuntimeFrame(f.Id,own with{MouGuoJia=own.MouGuoJia! with{After=m.Sequence}});});
  MouGuoJia.Continue(f.Id);return true;
 }
}
