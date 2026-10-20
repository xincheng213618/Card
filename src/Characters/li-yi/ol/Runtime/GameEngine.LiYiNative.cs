namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool IsChanshuangFactionOrigin(ProgramSkillFrame f,int actor,int target,string resultBind)=>IsChanshuangSlashRequest(f)&&TryGetChanshuangSlashRequest(f,out var actual,out var targets)&&actual==actor&&targets.SequenceEqual([target])&&resultBind==$"chanshuang-{f.Chanshuang!.Cursor}"&&GetFactionResponsePolicy(_players[actor],CardKind.Slash)is not null;
 private void BeginChanshuangFactionRequest(ProgramSkillFrame f,int actor,int target)
 {
  if(ActiveFactionCardRequest is not null||!IsChanshuangSlashRequest(f)||!TryGetChanshuangSlashRequest(f,out var actual,out var targets)||actual!=actor||!targets.SequenceEqual([target])||!CanRequestAssistedProgramFactionSlash(actor,target)||GetFactionResponsePolicy(_players[actor],CardKind.Slash)is not{}policy)throw new InvalidOperationException("Chanshuang lost its real requesting actor's native faction policy or ordinary Slash target.");
  var r=f.Chanshuang!;var candidates=GetFactionProviderSeats(actor,policy.FactionId);AdvanceEventRulesAndQueueFact(new ChanshuangFactionRequestedEvent(f.Id,r.Cursor,actor,target,policy));
  ActiveFactionCardRequest=new FactionCardRequestHandle(this,f.Id,FactionCardRequestPurpose.AssistedProgramUse,actor,candidates,policy.FactionId,targetSeat:target,programSkillFrameId:f.Id,policySource:policy,assistedResultBind:$"chanshuang-{r.Cursor}");ClearPendingDecision();_status=EngineStatus.Running;AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(f.Id,actor,candidates,policy.SkillId,IsActiveUse:true,target));AdvanceFactionSlashCandidate();
 }
 private bool ValidateChanshuangFactionSlashParent(FactionCardRequestHandle pending,ProgramSkillFrame f)=>TryGetChanshuangSlashRequest(f,out var actor,out var targets)&&f.Chanshuang is{}r&&pending.IsAssistedProgramUse&&pending.OwnerFrameId==f.Id&&pending.ProgramSkillFrameId==f.Id&&pending.OwnerSeat==actor&&targets is[var target]&&pending.TargetSeat==target&&pending.AssistedResultBind==$"chanshuang-{r.Cursor}"&&pending.PolicySource is{}policy&&pending.ProviderFactionId==policy.FactionId&&policy.RequiredKind==CardKind.Slash&&ProgramEventHistory<ChanshuangFactionRequestedEvent>().Count(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor&&e.ActorSeat==actor&&e.TargetSeat==target&&e.Source==policy)==1;
 private void PrepareChanshuangSuppliedSlash(ProgramSkillFrame supplied,int actorSeat,int providerSeat,int targetSeat,CardKind kind,IReadOnlyList<Card> materials,CardConversionSource? conversionSource,IReadOnlyList<CardConversionSource>? additionalConversionSources=null)
 {
  var f=GetActiveProgramFrame(supplied.Id);
  if(!IsChanshuangSlashRequest(f)||!TryGetChanshuangSlashRequest(f,out var actor,out var targets)||actor!=actorSeat||!targets.SequenceEqual([targetSeat])||!IsSlashCard(kind)||!_players[actorSeat].IsAlive||!_players[targetSeat].IsAlive||!IsValidPlayerSeat(providerSeat)||materials.Select(c=>c.Id).Distinct().Count()!=materials.Count||materials.Any(c=>c.Id<0||!_cardZones.CardsAt(_cardZones.GetLocation(c.Id)).Any(actual=>actual.Id==c.Id&&actual.Kind==c.Kind)))throw new InvalidOperationException("Chanshuang's real supplier lost its exact current actor, target or primary group.");
  var r=f.Chanshuang!;var sources=(conversionSource is{}source?new[]{source}:Array.Empty<CardConversionSource>()).Concat(additionalConversionSources??[]).ToArray();
  if(materials.Count==0&&sources.Length==0)throw new InvalidOperationException("Chanshuang cannot invent an unprovided zero-material Slash.");
  var selection=new ChanshuangSelection(actorSeat,providerSeat,targetSeat,kind){Materials=materials.Select(c=>new ChanshuangMaterial(c.Id,c.Kind,_cardZones.GetLocation(c.Id),c.IsGeneralWeapon)).ToArray(),Sources=sources};
  if(ProgramEventHistory<ChanshuangSlashPreparedEvent>().Any(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor))throw new InvalidOperationException("One Chanshuang execution cannot issue two native Slashes.");
  ReplaceRuntimeTop(f with{Chanshuang=r with{Stage=ChanshuangStage.SlashIssuing,Selection=selection}});AdvanceEventRulesAndQueueFact(new ChanshuangSlashPreparedEvent(f.Id,r.Cursor,actorSeat,providerSeat,targetSeat,kind,ChanshuangHash(selection)));
 }
 private bool FinishChanshuangSuppliedSlashFailure(ProgramSkillFrame f)
 {if(f.Chanshuang is null||!TryGetChanshuangSlashRequest(f,out _,out _))return false;f=GetActiveProgramFrame(f.Id);if(f.Chanshuang!.Native is not null)throw new InvalidOperationException("A completed native Slash cannot be cancelled as an unpaid request.");ClearPendingDecision();CompleteChanshuangItem(f,false);return true;}
 private void BindChanshuangSlashUse(CardUseFrame supplied)
 {
  var index=_resolutionStack.FindIndex(f=>f.Id==supplied.Id);if(index<1||_resolutionStack[index-1]is not ProgramSkillFrame{Chanshuang:{Stage:ChanshuangStage.SlashIssuing,Selection:{}selection}r}parent)return;
  var use=LifecycleCardUse(supplied.Id)!;
  if(use.ChanshuangSlash is not null||r.Native is not null||use.Action is not{Type:CardActionType.Use}a||use.SourceSeat!=selection.ActorSeat||a.ActorSeat!=selection.ActorSeat||a.ProviderSeat!=selection.ProviderSeat||use.CardKind!=selection.Kind||a.EffectiveKind!=selection.Kind||!use.TargetSeats.SequenceEqual([selection.TargetSeat])||!a.TargetSeats.SequenceEqual(use.TargetSeats)||!a.PhysicalCards.Select(c=>c.CardId).SequenceEqual(selection.Materials.Select(c=>c.CardId))||!a.ConversionChain.SequenceEqual(selection.Sources)||ProgramEventHistory<ChanshuangSlashPreparedEvent>().Count(e=>e.FrameId==parent.Id&&e.Cursor==r.Cursor&&e.SelectionHash==ChanshuangHash(selection))!=1)throw new InvalidOperationException("Chanshuang's native child changed its once-prepared actual supplier, conversion or payment.");
  var origin=new ChanshuangNativeOrigin(parent.Id,r.Cursor,r.Source,r.GameplayHash,r.ActualTurn,use.Id,a.ActionId,selection,a,_movementSequence);
  ReplaceRuntimeFrame(parent.Id,parent with{Chanshuang=r with{Stage=ChanshuangStage.NativeUse,Native=origin,NativeReturned=false,Before=_movementSequence,After=_movementSequence}});UpdateLifecycleCardUse(use.Id,u=>u with{ChanshuangSlash=origin});AdvanceEventRulesAndQueueFact(new ChanshuangSlashIssuedEvent(parent.Id,r.Cursor,use.Id,a.ActionId,ChanshuangHash(a)));
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27480)]
 private void RecordChanshuangSlashPayment(CardMovedEvent e)
 {
  if(!HasLiYiCapability||_cardMovements.LastOrDefault()is not{}m||m.CardId!=e.CardId||m.From!=e.From||m.To!=e.To||m.Reason!=e.Reason||m.Reason!=CardMoveReasons.Use)return;
  foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.ChanshuangSlash is not null).ToArray())
  {
   var r=use.ChanshuangSlash!;var material=r.OriginalAction.PhysicalCards.SingleOrDefault(c=>c.CardId==m.CardId);if(material is null||material.From!=m.From||m.Sequence<=r.BeforePayment||ProgramEventHistory<ChanshuangSlashMaterialPaidEvent>().Any(p=>p.UseId==use.Id&&p.CardId==m.CardId))continue;
   var to=r.Selection.Materials.Single(c=>c.CardId==m.CardId).GeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:IsProgramAlternativeCost(r.OriginalAction,m.CardId)?CardLocation.DrawPile:CardLocation.Processing;
   if(m.To==to)AdvanceEventRulesAndQueueFact(new ChanshuangSlashMaterialPaidEvent(use.Id,r.ActionId,m.CardId,m.From,m.To,m.Sequence));
  }
 }
 private bool IsIssuedChanshuangUse(CardUseFrame use)
 {
  if(use.ChanshuangSlash is not{}o||use.Action is not{Type:CardActionType.Use}a||use.Id!=o.UseId||a.ActionId!=o.ActionId||!IsSlashCard(use.CardKind)||!ShownEntityUseActorMatches(use,o.Selection.ActorSeat,o.Selection.ProviderSeat)||a.ProviderSeat!=o.Selection.ProviderSeat||!a.PhysicalCards.SequenceEqual(o.OriginalAction.PhysicalCards)||!use.PhysicalCardIds!.SequenceEqual(o.Selection.Materials.Select(c=>c.CardId))||ProgramEventHistory<ChanshuangSlashIssuedEvent>().Count(e=>e.FrameId==o.ProgramFrameId&&e.Cursor==o.Cursor&&e.UseId==use.Id&&e.ActionId==o.ActionId&&e.OriginalActionHash==ChanshuangHash(o.OriginalAction))!=1)return false;
  var original=a;if(use.CurrentSlashFirePolicy is{}fire){AssertCurrentSlashFirePolicy(use);original=fire.OriginalAction;}else if(use.CardKind!=o.Selection.Kind)return false;
  if(original.EffectiveKind!=o.Selection.Kind||!original.ConversionChain.SequenceEqual(o.Selection.Sources)||original.ActorSeat!=o.Selection.ActorSeat&&ProgramEventHistory<ProgramCardUseActorReplacedEvent>().All(e=>e.CardUseFrameId!=use.Id)||_resolutionStack.SingleOrDefault(f=>f.Id==o.ProgramFrameId)is not ProgramSkillFrame{Chanshuang:{}r}parent||r.Native!=o||r.Cursor!=o.Cursor||r.Source!=o.Source||r.GameplayHash!=o.GameplayHash||r.NativeReturned||r.Stage!=ChanshuangStage.NativeUse)return false;
  foreach(var cost in o.OriginalAction.PhysicalCards)
  {
   var paid=ProgramEventHistory<ChanshuangSlashMaterialPaidEvent>().Where(e=>e.UseId==use.Id&&e.CardId==cost.CardId).ToArray();
   if(paid.Length==0){if(_cardZones.GetLocation(cost.CardId)!=cost.From)return false;continue;}
   if(paid is not[var p]||p.ActionId!=o.ActionId||p.From!=cost.From||p.Sequence<=o.BeforePayment||_cardMovements.Count(m=>m.Sequence==p.Sequence&&m.CardId==p.CardId&&m.From==p.From&&m.To==p.To&&m.Reason==CardMoveReasons.Use)!=1)return false;
  }
  return true;
 }
 private bool HasIssuedChanshuangSlash(long? useId,int actor)=>useId is{}id&&LifecycleCardUse(id)is{ChanshuangSlash:{}origin}use&&origin.Selection.ActorSeat==actor&&IsIssuedChanshuangUse(use);
 private bool IsChanshuangRemovedMaterial(long useId,int cardId)=>LifecycleCardUse(useId)is{ChanshuangSlash:{}r}use&&IsIssuedChanshuangUse(use)&&_cardZones.GetLocation(cardId)==CardLocation.OutsideGame&&r.Selection.Materials.Any(c=>c.CardId==cardId&&c.GeneralWeapon&&c.From.Zone==CardZoneKind.Equipment)&&ProgramEventHistory<ChanshuangSlashMaterialPaidEvent>().Count(e=>e.UseId==useId&&e.CardId==cardId&&e.To==CardLocation.OutsideGame)==1;
 [NativeUseValidation(27480)]private void ValidateChanshuangNative(CardUseFrame use){if(use.ChanshuangSlash is not null&&!IsIssuedChanshuangUse(use))throw new InvalidOperationException("Chanshuang lost its actual issued native Slash, once-paid materials or typed parent.");}
 [NativeUseReturn(27480)]private void ReturnChanshuangNative(CardUseFrame use)
 {
  if(use.ChanshuangSlash is not{}o)return;var f=GetActiveProgramFrame(o.ProgramFrameId);var r=f.Chanshuang;
  if(r is not{Stage:ChanshuangStage.NativeUse,NativeReturned:false}||r.Native!=o||use.Step!=ResolutionFrameStep.Completed||LifecycleCardUse(use.Id)is not null||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)!=1)throw new InvalidOperationException("Chanshuang cannot return before or twice after its whole native Use.");
  ReplaceRuntimeTop(f with{Chanshuang=r with{NativeReturned=true}});if(use.CardAttack is not{Active:true})SettleChanshuangNative(o);
 }
 [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword,27480)]private bool ReturnChanshuangAttack(AttackCompletionReceipt completion)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Chanshuang:{NativeReturned:true,Native:{}o}}||completion.ResolutionId!=o.UseId)return false;
  if(completion.ProgramFrameId is{}parent&&parent!=o.ProgramFrameId)throw new InvalidOperationException("Chanshuang's native attack returned to another program.");SettleChanshuangNative(o);return true;
 }
 private void SettleChanshuangNative(ChanshuangNativeOrigin o)
 {
  var f=GetActiveProgramFrame(o.ProgramFrameId);var r=f.Chanshuang!;
  if(r.Native!=o||!r.NativeReturned||r.Cursor!=o.Cursor||LifecycleCardUse(o.UseId)is not null||ProgramEventHistory<ChanshuangSlashReturnedEvent>().Any(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor))throw new InvalidOperationException("Chanshuang lost its unique complete native return.");
  ReplaceRuntimeTop(f with{Chanshuang=r with{Stage=ChanshuangStage.NativeChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});AdvanceEventRulesAndQueueFact(new ChanshuangSlashReturnedEvent(f.Id,r.Cursor,o.UseId,o.ActionId));
 }
 private bool ResumeChanshuangSuppliedSlash(ProgramSkillFrame supplied)
 {
  var f=GetActiveProgramFrame(supplied.Id);if(f.Chanshuang is not{}r)return false;if(r.Native is{}o&&r.NativeReturned&&r.Stage==ChanshuangStage.NativeUse)SettleChanshuangNative(o);ContinueChanshuang(f.Id);return true;
 }
 private bool IsChanshuangNativeSettlementBatch(ProgramSkillFrame f,CardMovementBatchContext batch)=>f.Chanshuang is{Stage:ChanshuangStage.NativeChildren,NativeReturned:true,Native:{}o}r&&batch.ParentFrameId==o.UseId&&batch.AwaitingProgramFrameId is null&&batch.Movements.Count>0&&ProgramEventHistory<ChanshuangSlashReturnedEvent>().Count(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor&&e.UseId==o.UseId)==1&&batch.Movements.All(m=>m.Sequence>r.After&&_cardMovements.Contains(m)&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.UseFinished&&o.Selection.Materials.Any(c=>c.CardId==m.CardId&&c.Kind==m.CardKind));
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 60)]
    private bool HasIssuedChanshuangSlashDebitExemption(long useId, int actorSeat) =>
        HasIssuedChanshuangSlash(useId, actorSeat);

}
