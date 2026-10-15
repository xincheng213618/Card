namespace CardGame.Core;
public sealed partial class GameEngine
{
 private void CaptureCaoChunUsePaymentBatch(CardMovementBatchContext batch)
 {
  if(!HasCaoChunCapability)return;
  if(_resolutionStack.LastOrDefault()is not CardUseFrame{Action:{Type:CardActionType.Use}a}use||a.ActorSeat!=_currentSeat||a.ProviderSeat!=a.ActorSeat||_players[a.ActorSeat].Shanjia?.Debt is not{}d||d.Remaining<=0||d.ActualTurn!=_turnNumber||d.PhaseInstance!=_cardUseDebitPhaseInstanceId||batch.ParentFrameId!=use.Id||batch.ParentBatchId is not null||batch.Movements.Count!=a.PhysicalCards.Count||!a.PhysicalCards.Any(c=>c.From.OwnerSeat==a.ActorSeat&&c.From.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain))return;
  if(batch.Movements.Any(m=>!a.PhysicalCards.Any(c=>c.CardId==m.CardId&&c.CardKind==m.CardKind&&c.From==m.From))||batch.Movements.Any(m=>!_cardMovements.Contains(m)))return;
  if(use.CaoChunMaterialBatch is not null)throw new InvalidOperationException("Shanjia actual hand use cannot pay its same material batch twice.");UpdateLifecycleCardUse(use.Id,f=>f with{CaoChunMaterialBatch=batch});
 }
 private CaoChunHandUsePayment? CaoChunPaidUseQualification(CardActionContext action)
 {
  if(!HasCaoChunCapability)return null;
  var use=_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f=>f.Action?.ActionId==action.ActionId);if(use?.CaoChunMaterialBatch is not{}batch)return null;
  if(use.CaoChunDebtQualification is{}old)return CaoChunDebtCandidate(old)is not null?old:null;
  return CaptureCaoChunPaidHandUse(use.Id,action,batch.Movements.Min(m=>m.Sequence)-1,batch.Movements.Max(m=>m.Sequence));
 }
 private bool TryBeginCaoChunSimpleDebt(long id,ProgramSimpleCardContinuation continuation)
 {
  var use=LifecycleCardUse(id);if(use?.Action is not{}a||CaoChunPaidUseQualification(a)is not{}p)return false;
  return TryBeginProgramCardWindow(null,a,SkillProgramTriggerWindow.CardUseBeforeTargetEffects,a.TargetSeats,ProgramCardContinuation.CommittedSimpleCard,simpleContinuation:continuation,caoChunPayment:p,caoChunDebtOnly:true);
 }
 private bool CaoChunResponseParentMatches(CardAttackHandle?attack,CardActionContext action,ProgramCompletedResponseKind kind)
 {
  if(kind==ProgramCompletedResponseKind.Nullification)return ActiveNullificationWindow is{}n&&action.ParentActionId==NullificationParentActionId(n)&&action.OpponentSeat==n.SourceSeat;
  return attack is not null&&IsSlashCard(attack.EffectiveCardKind??CardKind.Slash)&&attack.TargetSeat==action.ActorSeat&&attack.SourceSeat==action.OpponentSeat&&LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId==action.ParentActionId;
 }
 private bool TryBeginCaoChunPaidResponseDebt(CardAttackHandle?attack,CardActionContext action,ProgramCardContinuation continuation)
 {
  if((!HasCaoChunCapability)||!IsCompletedResponseUse(action,continuation)||action.PhysicalCards.Count==0)return false;
  var kind=continuation==ProgramCardContinuation.Dodge?ProgramCompletedResponseKind.Dodge:ProgramCompletedResponseKind.Nullification;if(!CaoChunResponseParentMatches(attack,action,kind))return false;
  var reason=kind==ProgramCompletedResponseKind.Nullification&&action.PhysicalCards.Count==1?CardMoveReasons.Nullification:CardMoveReasons.Respond;
  var moves=action.PhysicalCards.Select(c=>_cardMovements.LastOrDefault(m=>m.CardId==c.CardId&&m.CardKind==c.CardKind&&m.From==c.From&&(m.To==CardLocation.Processing||IsProgramAlternativeCost(action,c.CardId)&&m.To==CardLocation.DrawPile)&&m.Reason==reason)).ToArray();if(moves.Any(m=>m is null))return false;
  var parent=kind==ProgramCompletedResponseKind.Nullification?ActiveNullificationWindow!.Id:attack!.ResolutionId;var p=CaptureCaoChunPaidHandUse(parent,action,moves.Min(m=>m!.Sequence)-1,moves.Max(m=>m!.Sequence),kind);if(p is null)return false;
  return TryBeginProgramCardWindow(attack,action,SkillProgramTriggerWindow.CardUseBeforeTargetEffects,[],continuation:null,completedResponseReturn:new(kind,IsCommitted:true),caoChunPayment:p,caoChunDebtOnly:true);
 }
}
