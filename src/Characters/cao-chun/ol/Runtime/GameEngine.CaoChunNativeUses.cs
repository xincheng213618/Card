namespace CardGame.Core;
public sealed partial class GameEngine
{
 private CardLocation CaoChunNativeCostDestination(CardActionContext action,CardActionCost cost)=>NormalizeDiouCarrierDestination(GetAdvancedCard(cost.CardId), cost.From, GetAdvancedCard(cost.CardId).IsGeneralWeapon&&cost.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:IsProgramAlternativeCost(action,cost.CardId)?CardLocation.DrawPile:CardLocation.Processing);
 private void BindCaoChunNativeUse(CardUseFrame use)
 {
  var i=_resolutionStack.FindIndex(x=>x.Id==use.Id);if(i<1||_resolutionStack[i-1]is not ProgramSkillFrame{CaoChun:{Stage:CaoChunStage.NativeSlash,Slash:{}n}r}parent)return;
  if(n.NativeUseId!=0){if(n.NativeUseId!=use.Id||use.CaoChunSlash!=n)throw new InvalidOperationException("Shanjia cannot bind another native Slash.");return;}
  if(use.Action is not{Type:CardActionType.Use}a||a.ActorSeat!=parent.OwnerSeat||a.ProviderSeat!=(n.ProviderSeat??parent.OwnerSeat)||a.EffectiveKind!=n.Kind||use.CardKind!=n.Kind||!use.TargetSeats.SequenceEqual([n.TargetSeat])||!a.PhysicalCards.Select(c=>(c.CardId,c.CardKind,c.From)).SequenceEqual(n.Materials.Select(c=>(c.CardId,c.CardKind,c.From)))||a.PhysicalCards.Count!=(n.Ending?0:n.Materials.Count)||n.ParentFrameId!=parent.Id||n.Source!=r.Source||n.GameplayHash!=r.GameplayHash||n.InstructionIndex!=parent.InstructionIndex||n.Ending&&use.CardId!=0||!n.Ending&&n.Materials.Count==0&&n.RoundOncePayment is null&&n.DrawFundedPayment is null&&n.ChainedPayment is null&&!HasWeijieSpecializedPayment(parent,use)&&!(HasZonghuSpecializedPayment(parent,use) || HasHuanpeiProgramPayment(parent,use)))throw new InvalidOperationException("Shanjia native Slash changed its accepted issuer, exact materials or target.");
  var bound=n with{NativeUseId=use.Id,ActionId=a.ActionId,Action=a,Materials=a.PhysicalCards,PaymentAfter=n.Materials.Count==0?n.PaymentBefore:null};ReplaceRuntimeFrame(parent.Id,parent with{CaoChun=r with{Slash=bound}});UpdateLifecycleCardUse(use.Id,f=>f with{CaoChunSlash=bound});AdvanceEventRulesAndQueueFact(new ShanjiaNativeSlashIssuedEvent(parent.Id,use.Id,a.ActionId,n.Ending,n.TargetSeat));if(n.Materials.Count==0)AdvanceEventRulesAndQueueFact(new ShanjiaNativeMaterialPaidEvent(parent.Id,use.Id,a.ActionId,n.PaymentBefore,n.PaymentBefore,0));
 }
 [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 20)]
 private void CompleteCaoChunNativePayment(long id)
 {
  if(LifecycleCardUse(id)is not{CaoChunSlash:{}n}use)return;
  if(n.Materials.Count==0)
  {
   if(_resolutionStack.SingleOrDefault(f=>f.Id==n.ParentFrameId)is not ProgramSkillFrame zeroParent||!CaoChunNativeRidesOn(zeroParent,use)||n.PaymentAfter!=n.PaymentBefore||!n.Ending&&n.RoundOncePayment is null&&n.DrawFundedPayment is null&&n.ChainedPayment is null&&!HasWeijieSpecializedPayment(zeroParent,use)&&!(HasZonghuSpecializedPayment(zeroParent,use) || HasHuanpeiProgramPayment(zeroParent,use))||CompleteProgramEventHistory().OfType<ShanjiaNativeMaterialPaidEvent>().Count(e=>e.FrameId==zeroParent.Id&&e.NativeUseId==id&&e.ActionId==n.ActionId&&e.Before==n.PaymentBefore&&e.After==n.PaymentBefore&&e.Count==0)!=1||n.RoundOncePayment is{}independent&&CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==independent)!=1)throw new InvalidOperationException("Shanjia zero-material native payment lost its exact issued independent or ending-use receipt.");
   return;
  }
  if(_resolutionStack.SingleOrDefault(f=>f.Id==n.ParentFrameId)is not ProgramSkillFrame{CaoChun:{}r}parent||n.PaymentAfter is not null||!CaoChunNativeRidesOn(parent,use))throw new InvalidOperationException("Shanjia native payment lost its actual bound request.");
  var after=_movementSequence;if(n.Materials.Any(c=>_cardMovements.Count(m=>m.Sequence>n.PaymentBefore&&m.Sequence<=after&&m.CardId==c.CardId&&m.From==c.From&&m.To==CaoChunNativeCostDestination(use.Action!,c)&&m.Reason==CardMoveReasons.Use)!=1))throw new InvalidOperationException("Shanjia actual requested Slash material was not paid exactly once.");
  var paid=n with{PaymentAfter=after};ReplaceRuntimeFrame(parent.Id,parent with{CaoChun=r with{Slash=paid}});UpdateLifecycleCardUse(id,f=>f with{CaoChunSlash=paid});AdvanceEventRulesAndQueueFact(new ShanjiaNativeMaterialPaidEvent(parent.Id,id,n.ActionId,n.PaymentBefore,after,n.Materials.Count));
 }
 private bool CaoChunNativeRidesOn(ProgramSkillFrame parent,CardUseFrame use)
 {
  if(parent.CaoChun is not{Stage:CaoChunStage.NativeSlash,Slash:{}n}r||use.CaoChunSlash!=n||n.ParentFrameId!=parent.Id||n.Source!=r.Source||n.GameplayHash!=parent.GameplayHash||n.NativeUseId!=use.Id||n.ActionId!=use.Action?.ActionId||n.Action is not{}original||use.Action is not{}a||original.ActionId!=a.ActionId||original.ParentActionId!=a.ParentActionId||original.ProviderSeat!=a.ProviderSeat||!a.PhysicalCards.SequenceEqual(n.Materials)||!a.ConversionChain.SequenceEqual(original.ConversionChain)||!ShownEntityUseActorMatches(use,parent.OwnerSeat,n.ProviderSeat??parent.OwnerSeat)||!IsSlashCard(use.CardKind)||!IsSlashCard(a.EffectiveKind)||n.Ending&&n.Materials.Count!=0)return false;
  if(n.DrawFundedPayment is{}funded&&(use.DrawFundedDistinctBasicUse?.Payment!=funded||!ValidDrawFundedDistinctBasicPayment(funded)))return false;
  if(n.ChainedPayment is{}chained&&(use.ChainedStateBasicUse?.Payment!=chained||!ValidChainedStateBasicPayment(chained)))return false;
  return CompleteProgramEventHistory().OfType<ShanjiaNativeSlashIssuedEvent>().Count(e=>e.FrameId==parent.Id&&e.NativeUseId==use.Id&&e.ActionId==n.ActionId&&e.Ending==n.Ending&&e.TargetSeat==n.TargetSeat)==1;
 }
 [NativeUseReturn(30)]
 private bool CompleteCaoChunUseReturn(CardUseFrame use)
 {
     if (use.CaoChunSlash is null) return false;
     ReturnDrawFundedDistinctBasicUse(use);
     ReturnChainedStateBasicUse(use);
     return true;
 }
 [NativeUseValidation(70)]
 private void AssertCaoChunNativeUse(CardUseFrame use)
 {
  if(use.CaoChunSlash is not{}n)return;if(_resolutionStack.SingleOrDefault(f=>f.Id==n.ParentFrameId)is not ProgramSkillFrame parent||!CaoChunNativeRidesOn(parent,use))throw new InvalidOperationException("Shanjia lost its exact owned native Slash or legitimate actor replacement.");
 }
 // Native AttackCompletionReceipt is captured before its actual CardUseFrame is removed.
 [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 140)]
 private bool CompleteCaoChunSlash(AttackCompletionReceipt receipt)
 {
  if(receipt.ProgramFrameId is not{}parentId||_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoChun:{Stage:CaoChunStage.NativeSlash,Slash:{}n}r}parent||parent.Id!=parentId)return false;
  if(receipt.CaoChunSlash!=n||n.NativeUseId!=receipt.ResolutionId||n.Returned||n.Action is null||LifecycleCardUse(n.NativeUseId)is not null||CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e=>e.ResolutionId==n.NativeUseId)!=1||CompleteProgramEventHistory().OfType<ShanjiaNativeSlashIssuedEvent>().Count(e=>e.FrameId==parent.Id&&e.NativeUseId==n.NativeUseId&&e.ActionId==n.ActionId)!=1)throw new InvalidOperationException("Shanjia Slash must finish and return once through its original native attack receipt.");
  if(n.RoundOncePayment is{}independent&&(n.Action.ConversionChain is not[var actualSource]||actualSource!=independent.Source||CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==independent)!=1))throw new InvalidOperationException("Shanjia independent Slash lost its real once-round payment.");
  if(n.PaymentAfter is not{}after||CompleteProgramEventHistory().OfType<ShanjiaNativeMaterialPaidEvent>().Count(e=>e.FrameId==parent.Id&&e.NativeUseId==n.NativeUseId&&e.ActionId==n.ActionId&&e.Before==n.PaymentBefore&&e.After==after&&e.Count==n.Materials.Count)!=1)throw new InvalidOperationException("Shanjia Slash return lost its exact native material invoice.");
  foreach(var cost in n.Materials)if(_cardMovements.Count(m=>m.Sequence>n.PaymentBefore&&m.Sequence<=after&&m.CardId==cost.CardId&&m.From==cost.From&&m.To==CaoChunNativeCostDestination(n.Action,cost)&&m.Reason==CardMoveReasons.Use)!=1)throw new InvalidOperationException("Shanjia requested Slash cannot return without its actual material payment.");
  AdvanceEventRulesAndQueueFact(new ShanjiaNativeSlashReturnedEvent(parent.Id,n.NativeUseId,n.ActionId));ReplaceRuntimeTop(parent with{CaoChun=r with{Slash=n with{Returned=true}}});ResumeCaoChun(parent.Id);return true;
 }
 private bool IsCaoChunNativeEdge(int index)
 {
  if(index<1||index>=_resolutionStack.Count||_resolutionStack[index-1]is not ProgramSkillFrame parent||_resolutionStack[index]is not CardUseFrame use||use.CaoChunSlash is null)return false;return CaoChunNativeRidesOn(parent,use);
 }
}
