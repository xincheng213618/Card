namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasCaoYuCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGongjieRoundFirstGifts)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXiangxuMinimumTransition)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveXiangzuDyingGiftAndRecovery);
 private static CardConversionSource CaoYuSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private bool CaoYuActualTurnOpen(int turn,int owner)=>turn>0&&turn==_turnNumber&&turn==_turnProgression.TurnNumber&&owner==_turnProgression.OwnerSeat&&!ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e=>e.ActualTurn==turn&&e.OwnerSeat==owner);
 private bool CaoYuHistory(int owner,string skill,int recipient)=>ProgramEventHistory<CaoYuRecipientHistoryEvent>().Any(e=>e.OwnerSeat==owner&&e.SkillId==skill&&e.RecipientSeat==recipient);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27440)]
 private bool QualifyCaoYu(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
 {
  if(t.Effects is not[{ }e]||!CaoYuComposition.Handles(e.Op))return true;
  return e.Op switch{SkillProgramEffectOp.ResolveGongjieRoundFirstGifts=>CaoYuRoundFirst(c,context),SkillProgramEffectOp.ResolveXiangxuMinimumTransition=>IsXiangxuMinimumCandidate(c,context),SkillProgramEffectOp.ResolveXiangxuIssuedTurnEndAdjustment=>IsIssuedXiangxuEndingCandidate(c,context),_=>context.Window==SkillProgramTriggerWindow.DyingEntering&&context.TargetSeat==c.OwnerSeat&&_players[c.OwnerSeat].IsAlive&&!XiangzuSpent(c.OwnerSeat)&&CaoYuGiftCards(c.OwnerSeat,c.OwnerSeat).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat)};
 }
}
