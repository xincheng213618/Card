namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string CaoChunFactionBind="shanjia-initial-faction";
 private bool ValidCaoChunPaidActivation(ProgramSkillFrame f)
 {
  if(f.CaoChun is not{}r||f.ActivationId!="draw-and-slash"||f.InstructionIndex!=1||r.Source!=CaoChunSource(f)||r.GameplayHash!=f.GameplayHash||r.ActualTurn!=_turnNumber||r.PhaseInstance!=_cardUseDebitPhaseInstanceId)return false;
  var facts=CompleteProgramEventHistory().OfType<ShanjiaMovementPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Stage==CaoChunStage.DrawChildren).ToArray();
  return facts is[var draw]&&draw.Before<=draw.After&&draw.After<=_movementSequence&&draw.ActualCount is >=0 and <=3&&_cardMovements.Count(m=>m.Sequence>draw.Before&&m.Sequence<=draw.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ShanjiaDrawReason)==draw.ActualCount;
 }
 private bool IsCaoChunInitialRequest(ProgramSkillFrame f)=>f.CaoChun?.Stage==CaoChunStage.InitialSlash&&ValidCaoChunPaidActivation(f)&&_phase==TurnPhase.Play&&_currentSeat==f.OwnerSeat;
 private bool CaoChunInitialAdapterActor(ProgramSkillFrame f,int actor,int?target,int cursor,long?parent,long?request)=>f.Id==parent&&f.Id==request&&f.InstructionIndex==cursor&&actor==f.OwnerSeat&&target is{}seat&&IsValidPlayerSeat(seat)&&seat!=actor&&ValidCaoChunPaidActivation(f)&&f.CaoChun is{Stage:CaoChunStage.InitialSlash or CaoChunStage.NativeSlash}r&&(r.Slash is null||r.Slash.TargetSeat==seat);
 private void PrepareCaoChunDrawFundedSlash(ProgramSkillFrame f,DrawFundedDistinctBasicPayment p)
 {
  if(!IsCaoChunInitialRequest(f)||!ValidDrawFundedDistinctBasicPayment(p)||!CaoChunInitialAdapterActor(f,p.Source.OwnerSeat,p.TargetSeat,p.Cursor,p.ParentFrameId,p.RequestFrameId)||p.Intent!=DrawFundedDistinctBasicIntent.AssistedSlash)throw new InvalidOperationException("Shanjia draw-funded initial Slash lost its exact native paid need.");
  var r=f.CaoChun!;var n=new CaoChunNativeSlash(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,p.TargetSeat!.Value,p.EffectiveKind){DrawFundedPayment=p,PaymentBefore=_movementSequence};ReplaceRuntimeTop(f with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=n}});
 }
 private void PrepareCaoChunChainedSlash(ProgramSkillFrame f,ChainedStateBasicPayment p)
 {
  if(!IsCaoChunInitialRequest(f)||!ValidChainedStateBasicPayment(p)||!CaoChunInitialAdapterActor(f,p.Source.OwnerSeat,p.TargetSeat,p.Cursor,p.ParentFrameId,p.RequestFrameId)||p.Intent!=ChainedStateBasicIntent.AssistedSlash)throw new InvalidOperationException("Shanjia chained initial Slash lost its actual paid state-change need.");
  var r=f.CaoChun!;var n=new CaoChunNativeSlash(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,p.TargetSeat!.Value,p.EffectiveKind){ChainedPayment=p,PaymentBefore=_movementSequence};ReplaceRuntimeTop(f with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=n}});
 }
 private void BeginCaoChunFactionSlash(ProgramSkillFrame f,int target)
 {
  if(!IsCaoChunInitialRequest(f)||!CaoChunSourceCurrent(f)||ActiveFactionCardRequest is not null||!CanRequestAssistedProgramFactionSlash(f.OwnerSeat,target))throw new InvalidOperationException("Shanjia faction request lost its current exact native need.");
  var policy=GetFactionResponsePolicy(_players[f.OwnerSeat],CardKind.Slash)!;ReplaceRuntimeTop(f=f with{CaoChun=f.CaoChun!with{Faction=new(f.Id,target,policy)}});var seats=GetFactionProviderSeats(f.OwnerSeat,policy.FactionId);
  ActiveFactionCardRequest=new FactionCardRequestHandle(this,f.Id,FactionCardRequestPurpose.AssistedProgramUse,f.OwnerSeat,seats,policy.FactionId,targetSeat:target,programSkillFrameId:f.Id,policySource:policy,assistedResultBind:CaoChunFactionBind);_status=EngineStatus.Running;AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id,f.OwnerSeat,seats,policy.SkillId,IsActiveUse:true,target));AdvanceFactionSlashCandidate();
 }
 private bool ValidateCaoChunFactionSlashParent(FactionCardRequestHandle pending,ProgramSkillFrame f)
 {
  if(f.CaoChun?.Faction is not{}issued)return false;
  if(!ValidCaoChunPaidActivation(f)||issued.FrameId!=f.Id||pending.ProgramSkillFrameId!=f.Id||pending.ResolutionId!=f.Id||pending.OwnerSeat!=f.OwnerSeat||pending.TargetSeat!=issued.TargetSeat||pending.PolicySource!=issued.Policy||pending.AssistedResultBind!=CaoChunFactionBind||!pending.IsAssistedProgramUse||f.CaoChun.Stage is not(CaoChunStage.InitialSlash or CaoChunStage.NativeSlash)||CompleteProgramEventHistory().OfType<FactionSlashRequestedEvent>().Count(e=>e.ResolutionId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.TargetSeat==issued.TargetSeat&&e.SkillId==issued.Policy.SkillId)!=1)throw new InvalidOperationException("Shanjia faction Slash lost its exact source policy, owner or requested target.");return true;
 }
 private bool PrepareCaoChunFactionNative(ProgramSkillFrame f,FactionCardRequestHandle pending,CharacterState provider,IReadOnlyList<Card>cards,CardKind kind)
 {
  if(!ValidateCaoChunFactionSlashParent(pending,f))return false;if(!IsCaoChunInitialRequest(f)||cards.Count==0||provider.Seat==f.OwnerSeat||!pending.CandidateSeats.Contains(provider.Seat)||cards.Any(c=>_cardZones.GetLocation(c.Id).OwnerSeat!=provider.Seat))throw new InvalidOperationException("Shanjia faction provider lost its real owned Slash material.");
  var r=f.CaoChun!;var native=new CaoChunNativeSlash(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,pending.TargetSeat!.Value,kind){ProviderSeat=provider.Seat,Materials=cards.Select(c=>new CardActionCost(c.Id,c.Kind,_cardZones.GetLocation(c.Id))).ToArray(),PaymentBefore=_movementSequence};ReplaceRuntimeTop(f with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=native}});return true;
 }
 private bool FinishCaoChunFactionDeclined(long id,FactionCardRequestHandle pending)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||f.Id!=id||f.CaoChun?.Faction is null)return false;ValidateCaoChunFactionSlashParent(pending,f);FinishCaoChun(f);return true;
 }
 private void DispatchCaoChunRequestedDeckSlash(ProgramSkillFrame f,Card card,IReadOnlyList<int>targets)
 {
  if(!IsCaoChunInitialRequest(f)||f.RequestedDeckBasicMaterial is not{Claiming:true,SelectedCardId:{}id}material||id!=card.Id||material.ActorSeat!=f.OwnerSeat||material.Intent!=RequestedDeckBasicIntent.AssistedSlash||!HasValidRequestedDeckBasicMaterial(f)||targets is not[var target]||!CanUseSlashTarget(_players[f.OwnerSeat],_players[target],card,effectiveKind:card.Kind,physicalCardIds:[card.Id]))throw new InvalidOperationException("Shanjia deck request lost its exact selected real native material.");
  var r=f.CaoChun!;var n=new CaoChunNativeSlash(f.Id,r.Source,r.GameplayHash,r.InstructionIndex,r.ActualTurn,r.PhaseInstance,false,target,card.Kind){Materials=[new(card.Id,card.Kind,CardLocation.DrawPile)],PaymentBefore=_movementSequence};ReplaceRuntimeTop(f with{CaoChun=r with{Stage=CaoChunStage.NativeSlash,Slash=n}});ClearPendingDecision();ResolveSlashCore(_players[f.OwnerSeat],_players[target],card,card.Kind,f.OwnerSeat,countsTowardSlashLimit:false,programSkillCardUseFrameId:f.Id);
 }
}
