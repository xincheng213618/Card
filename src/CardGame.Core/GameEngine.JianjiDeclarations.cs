namespace CardGame.Core;
public sealed partial class GameEngine
{
 // This bridge recognizes only the native declaration created by the already-paid
 // Jianji exact-card request. The declaration frame itself owns payment and return.
 private bool MatchesJianjiDeclaration(ProgramSkillFrame root,CardDeclarationFrame declaration)
 {
  if(root.HuangQuan is not{Kind:"jianji",Stage:"issuing",NativeUse:{UseId:0,ActionId:0}r}p || !ValidJianjiReceipt(r,root,false) ||
   r.ActorSeat!=p.RecipientSeat || declaration.OwnerSeat!=r.ActorSeat || declaration.TurnNumber!=p.ActualTurn || p.ActualTurn!=_turnNumber || p.TurnOwner!=_currentSeat || p.ActualPhase!=_cardUseDebitPhaseInstanceId ||
   declaration.Return is not{Purpose:CardDeclarationPurpose.Use,UseAction:{}a}ret || ret.ParentFrameId!=root.Id || ret.ParentKind!=root.Kind || ret.ParentStep!=root.Step || ret.ActorSeat!=r.ActorSeat || !SameJianjiReceipt(r,r with{Action=a}) ||
   declaration.Payment is not{Claimed:false}payment || payment.DeclarationId!=declaration.Id || payment.OwnerFrameId!=declaration.Id || payment.ProviderSeat!=r.ActorSeat || payment.ActorSeat!=r.ActorSeat || payment.Cost.CardId!=r.CardId ||
   payment.Cost.From!=CardLocation.Hand(r.ActorSeat) || r.Action.ConversionSource is not{}source || payment.Source!=source || source.OwnerSeat!=r.ActorSeat ||
   ViewAsRule(source) is not{DeclarationValidation:not null}rule || rule.OutputKind!=payment.DeclaredKind || !declaration.TargetSeats.SequenceEqual(a.TargetSeats))return false;
  // The generic native declaration invariant checks quota and challenge children.
  // Require the actual unique native commit and material movement as our join proof.
  return CompleteProgramEventHistory().OfType<CardDeclarationCommittedEvent>().Count(e=>e.DeclarationId==declaration.Id && e.OwnerSeat==r.ActorSeat && e.ActorSeat==r.ActorSeat && e.DeclaredKind==payment.DeclaredKind && e.TargetSeats.SequenceEqual(a.TargetSeats))==1 &&
   _cardMovements.Count(m=>m.Sequence>p.After && m.CardId==r.CardId && m.From==payment.Cost.From && m.To==CardLocation.Processing && m.Reason.Value=="conversion.declaration.pay")==1;
 }
 private bool IsJianjiDeclarationQuery(CharacterState actor,int? selectedId)
 {
  if(selectedId is not{}id || !actor.IsAlive || _resolutionStack.LastOrDefault() is not CardDeclarationFrame{Stage:CardDeclarationStage.Applying,Succeeded:true}declaration ||
   _resolutionStack.Count<2 || _resolutionStack[^2] is not ProgramSkillFrame root || !MatchesJianjiDeclaration(root,declaration) ||
   declaration.OwnerSeat!=actor.Seat || declaration.Payment.Cost.CardId!=id || _cardZones.GetLocation(id)!=CardLocation.Processing || UnclaimedDeclarationPayment(actor.Seat,id)!=declaration.Payment)return false;
  return true;
 }
 private bool IsJianjiDeclarationChild(ProgramSkillFrame root,ResolutionFrame child)=>child is CardDeclarationFrame declaration && MatchesJianjiDeclaration(root,declaration);
 private bool HuangQuanDeclarationObserverSuffix(int rootIndex)=>HasObserverSuffix(rootIndex,
  static(e,index)=>e.IsSameNameHandObserverEdge(index)||e.IsJianjiDeclarationObserverEdge(index),
  static(e,index,dying)=>e.IsSuspendedOrPaidRescueObserverSuffix(index,dying)||e.TieredRoundZeroDyingRescueRide(index,dying)||e.DrawFundedDistinctBasicDyingRescueRide(index,dying));
 private bool IsJianjiDeclarationObserverEdge(int index)
 {
  if(index<1 || _resolutionStack[index-1] is not CardDeclarationFrame d || _resolutionStack.SingleOrDefault(f=>f.Id==d.Return.ParentFrameId) is not ProgramSkillFrame root || !MatchesJianjiDeclaration(root,d))return false;
  if(_resolutionStack[index] is CardDeclarationChallengeFrame challenge)return d.Stage==CardDeclarationStage.Challenging && challenge.ParentFrameId==d.Id && challenge.SeatIndex>=0 && challenge.SeatIndex<challenge.EligibleSeats.Count && challenge.EligibleSeats.Distinct().Count()==challenge.EligibleSeats.Count && challenge.EligibleSeats.All(seat=>IsValidPlayerSeat(seat) && seat!=d.OwnerSeat);
  if(_resolutionStack[index] is CardsMovedTriggerWindowFrame moved)return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning && d.ActiveChildFrameId==moved.Id && moved.ResumeDeclarationFrameId==d.Id && moved.Batch.ParentFrameId==d.Id && moved.Batch.Movements.Count==1 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.CardId==d.Payment.Cost.CardId &&
   (d.Stage==CardDeclarationStage.Paying?m.From==d.Payment.Cost.From && m.To==CardLocation.Processing && m.Reason.Value=="conversion.declaration.pay":m.From==CardLocation.Processing && m.To==CardLocation.DiscardPile && m.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled"));
  return _resolutionStack[index] is ProgramLifecycleTriggerWindowFrame skills && d.Stage==CardDeclarationStage.Granting && skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==d.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeCardDeclaration;
 }
 private bool TryReturnJianjiDeclaration(CardDeclarationFrame declaration,bool success)
 {
  if(success || declaration.Return.Purpose!=CardDeclarationPurpose.Use || _resolutionStack.SingleOrDefault(f=>f.Id==declaration.Return.ParentFrameId) is not ProgramSkillFrame{HuangQuan:{Kind:"jianji",Stage:"issuing"}p}root)return false;
  if(_resolutionStack.Any(f=>f.Id==declaration.Id) || _resolutionStack.LastOrDefault()?.Id!=root.Id || declaration.Stage!=CardDeclarationStage.Returning || !MatchesJianjiDeclaration(root,declaration) ||
   CompleteProgramEventHistory().OfType<JianjiUseIssuedEvent>().Any(e=>e.Receipt.ProgramFrameId==root.Id) || CompleteProgramEventHistory().OfType<HuangQuanCompletedEvent>().Any(e=>e.FrameId==root.Id))throw new InvalidOperationException("Jianji declaration failure lost its original paid request or returned twice.");
  if(declaration.ChallengerSeat is{}challenger && CompleteProgramEventHistory().OfType<CardDeclarationRevealedEvent>().Count(e=>e.DeclarationId==declaration.Id && e.ChallengerSeat==challenger && e.Succeeded==declaration.Succeeded && e.Card.Id==declaration.Payment.Cost.CardId)!=1)
   throw new InvalidOperationException("Jianji declaration failure lost its actual challenged reveal.");
  // A failed challenge produced no Use. Clear only this preliminary use intent;
  // FinishHuangQuan then emits the normal once-only completion with a null action.
  ReplaceRuntimeTop(root=root with{HuangQuan=p with{Stage="offer",NativeUse=null}});FinishHuangQuan(root);return true;
 }
}
