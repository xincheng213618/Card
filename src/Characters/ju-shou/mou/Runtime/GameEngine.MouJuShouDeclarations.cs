namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MatchesMouJsDeclaration(ProgramSkillFrame f,CardDeclarationFrame d)
 {
  if(!MouJuShou.Valid(f)||f.MouJuShou is not{Stage:MouJuShouStage.Declaration,Native:{UseId:0,ActionId:0,Plan:{Form:"native",Cards:[var cardId],Action:{ConversionSource:{}source}}plan}n}r||d.Return is not{Purpose:CardDeclarationPurpose.MouJuShouUse,UseAction:{}action}ret||ret.ParentFrameId!=f.Id||ret.ParentKind!=f.Kind||ret.ParentStep!=f.Step||ret.ActorSeat!=r.Actor||ret.FinalEffectiveKind!=plan.Kind||!MouJuShouRuntime.SamePlan(plan,plan with{Action=action})||d.OwnerSeat!=r.Actor||d.TurnNumber!=r.Turn||d.Payment is not{Claimed:false}p||p.DeclarationId!=d.Id||p.OwnerFrameId!=d.Id||p.ActorSeat!=r.Actor||p.ProviderSeat!=r.Actor||p.Cost.CardId!=cardId||p.Cost.From!=CardLocation.Hand(r.Actor)||p.Source!=source||ViewAsRule(source)is not{DeclarationValidation:not null}rule||rule.OutputKind!=p.DeclaredKind||!d.TargetSeats.SequenceEqual(plan.Targets))return false;
  return ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e=>e.DeclarationId==d.Id&&e.OwnerSeat==r.Actor&&e.ActorSeat==r.Actor&&e.DeclaredKind==p.DeclaredKind&&e.TargetSeats.SequenceEqual(plan.Targets))==1&&_cardMovements.Count(m=>m.Sequence>r.Before&&m.CardId==cardId&&m.From==p.Cost.From&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay")==1;
 }
 private bool ReturnMouJsDeclaration(CardDeclarationFrame d,bool succeeded)
 {
  if(d.Return.Purpose!=CardDeclarationPurpose.MouJuShouUse)return false;
  if(_resolutionStack.Any(x=>x.Id==d.Id)||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||f.Id!=d.Return.ParentFrameId||d.Stage!=CardDeclarationStage.Returning||!MatchesMouJsDeclaration(f,d)||ProgramEventHistory<MouJsDeclarationReturnedEvent>().Any(e=>e.DeclarationId==d.Id))throw new InvalidOperationException("Zuo Jun declaration returned to another or already completed native request.");
  var r=f.MouJuShou!;if(succeeded)
  {
   if(d.Succeeded!=true||_cardZones.GetLocation(d.Payment.Cost.CardId)!=CardLocation.Processing||f.AcceptedDeclarationPayment is{Claimed:false})throw new InvalidOperationException("Zuo Jun cannot fake a passed declaration or repeat its actual cost.");
   ReplaceRuntimeFrame(f.Id,f=f with{AcceptedDeclarationPayment=d.Payment with{OwnerFrameId=f.Id},MouJuShou=r with{Stage=MouJuShouStage.IssuingUse,DeclarationId=d.Id,DeclarationPassed=true}});
  }
  else ReplaceRuntimeFrame(f.Id,f=f with{MouJuShou=r with{Stage=MouJuShouStage.ReturnedUse,Native=null,DeclarationId=d.Id,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
  AdvanceEventRulesAndQueueFact(new MouJsDeclarationReturnedEvent(f.Id,d.Id,succeeded));if(succeeded)IssueMouJsUse(f);else MouJuShou.Continue(f.Id);return true;
 }
 private bool MouJsDeclarationEdge(int i)
 {
  if(i<1||_resolutionStack[i-1]is not CardDeclarationFrame d||_resolutionStack.SingleOrDefault(x=>x.Id==d.Return.ParentFrameId)is not ProgramSkillFrame f||!MatchesMouJsDeclaration(f,d))return false;
  if(_resolutionStack[i]is CardDeclarationChallengeFrame c)return d.Stage==CardDeclarationStage.Challenging&&c.ParentFrameId==d.Id&&c.SeatIndex>=0&&c.SeatIndex<c.EligibleSeats.Count&&c.EligibleSeats.Distinct().Count()==c.EligibleSeats.Count&&c.EligibleSeats.All(s=>IsValidPlayerSeat(s)&&s!=d.OwnerSeat);
  if(_resolutionStack[i]is CardsMovedTriggerWindowFrame m)return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning&&d.ActiveChildFrameId==m.Id&&m.ResumeDeclarationFrameId==d.Id&&m.Batch.ParentFrameId==d.Id&&m.Batch.Movements is[var move]&&_cardMovements.Contains(move)&&move.CardId==d.Payment.Cost.CardId&&(d.Stage==CardDeclarationStage.Paying?move.From==d.Payment.Cost.From&&move.To==CardLocation.Processing&&move.Reason.Value=="conversion.declaration.pay":move.From==CardLocation.Processing&&move.To==CardLocation.DiscardPile&&move.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
  return _resolutionStack[i]is ProgramLifecycleTriggerWindowFrame skills&&d.Stage==CardDeclarationStage.Granting&&skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.ResumeProgramFrameId==d.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeCardDeclaration;
 }
 private bool FinishMouJsAlcoholCost(CardUseFrame use,Card card)
 {
  if(use.MouJuShouZuoJun is not{Plan:{Form:"multi",Kind:CardKind.Alcohol}plan}n)return false;
  var parent=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==n.FrameId);if(parent is null||!MouJsNativeRidesOn(use,parent)||!plan.Cards.Contains(card.Id))throw new InvalidOperationException("Zuo Jun lost its exact native multi-material Alcohol costs.");
  foreach(var id in plan.Cards)if(_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(c=>c.Id==id)is{}cost)MoveCard(cost,CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.UseFinished);return true;
 }
}
