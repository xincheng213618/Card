namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouJiangJiRuntime MouJiangJi=>GetRuntimeModule<MouJiangJiRuntime>();
 [ProgramActionSource(ProgramActionSourcePhase.ProgramTail,36300)]private IEnumerable<LegalAction> MouJjForeignActions(CharacterState actor)=>MouJiangJi.ForeignActions(actor.Seat);
 [ProgramActionHandler(ProgramActionHandlerKind.Submit,36300)]private bool SubmitMouJjForeign(UseProgramSkillCommand command,out CommandResult result)
 {
  result=null!;if(command.SkillId!=MouJiangJiRuntime.Shiju||command.ActivationId!="give-and-use"||command.SkillOwnerSeat is not{}owner)return false;
  var error=ValidateHumanPrompt(command.ActorSeat,DecisionKind.PlayCard,command.PromptId,CommandErrorCode.IllegalAction);
  if(error is not null){result=Reject(error.Code,error.Message);return true;}
  if(command.CardIds.Count!=0||command.TargetSeats.Count!=0||!MouJiangJi.ForeignActions(command.ActorSeat).Any(a=>a.ProgramSkillOwnerSeat==owner)){result=Reject(CommandErrorCode.IllegalAction,"Shiju requires its current published donor, owner and actual Play.");return true;}
  result=Accept(()=>{ClearPendingDecision();IssueMouJjForeign(command.ActorSeat,owner);AdvanceRulesAndPublishState();if(_options.AdvanceAfterHumanCommands)AdvanceToHumanBoundary();});return true;
 }
 [ProgramActionHandler(ProgramActionHandlerKind.Execute,36300)]private bool ExecuteMouJjForeign(CharacterState actor,LegalAction action,IReadOnlyList<int> cards,IReadOnlyList<int> targets)
 {
  if(action.ProgramSkillId!=MouJiangJiRuntime.Shiju||action.ProgramActivationId!="give-and-use"||action.ProgramSkillOwnerSeat is not{}owner)return false;
  if(cards.Count!=0||targets.Count!=0||!MouJiangJi.ForeignActions(actor.Seat).Any(a=>a.ProgramSkillOwnerSeat==owner))throw new InvalidOperationException("Shiju changed its published foreign owner or actual giver.");IssueMouJjForeign(actor.Seat,owner);return true;
 }
 private void IssueMouJjForeign(int actor,int owner)
 {
  var program=GetEnabledSkillProgram(_players[owner],MouJiangJiRuntime.Shiju);var f=new ProgramSkillFrame(++_resolutionSequence,owner,program.Id,"give-and-use",program.GameplayHash,0,[],[]){SkillInstanceId=GetRuntimeSkillInstanceId(_players[owner],program.Id),ActivatingSeat=actor,ActivationPhase=new(_turnNumber,actor,_cardUseDebitPhaseInstanceId)};
  PushRuntimeFrame(f);AdvanceEventRulesAndQueueFact(new ProgramSharedActivationStartedEvent(f.Id,new(f.SkillId,f.ActivationId,owner,f.SkillInstanceId),actor,f.ActivationPhase));AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id,owner,f.SkillId,f.ActivationId));AdvanceRuntimeProgram(f.Id);
 }
 [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity,36300)]private bool MouJjFinalized(ProgramTargetCardWindowQuery q)=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouJiangJiYingshi)&&IsOrdinaryTrick(q.Kind);
 [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo,36300)][ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo,36300)]private bool MouJjImplicit(ProgramTargetCardWindowQuery q)=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouJiangJiYingshi);
 [ProgramCapability]private sealed class MouJiangJiFacts(GameEngine e):IMouJiangJiFacts
 {
  public int Turn=>e._turnNumber;public int TurnOwner=>e._turnProgression.OwnerSeat;public long PhaseId=>e._cardUseDebitPhaseInstanceId;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;
  public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive;public bool Human(int s)=>e._players[s].IsHuman;public string Name(int s)=>e._players[s].Name;public int EquipmentCount(int s)=>Alive(s)?e.GetEquipment(e._players[s]).Count:0;
  public bool ActualPlay(int s)=>Alive(s)&&e._currentSeat==s&&e._phase==TurnPhase.Play&&PhaseId>0;
  public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();
  public IReadOnlyList<int> ShijuOwners(int actor)=>!ActualPlay(actor)||Cards(actor,false).Count==0?[]:Array.AsReadOnly(e._players.Where(p=>p.IsAlive&&e.EnabledSkillPrograms(p).Any(s=>s.Id==MouJiangJiRuntime.Shiju)).Select(p=>p.Seat).ToArray());
  public IReadOnlyList<MouJiangJiCard> Cards(int seat,bool discard)=>!Alive(seat)?[]:Array.AsReadOnly(new[]{CardZoneKind.Hand,CardZoneKind.Equipment}.SelectMany(z=>e._cardZones.CardsAt(new(z,seat)).Where(c=>!e.IsForeignEquipmentDiscardPrevented(seat,c,new(z,seat),discard?OwnedCardMoveIntent.Discard:OwnedCardMoveIntent.Transfer)&&(!discard||!e.IsSelfHandCategoryDiscardForbidden(seat,c,new(z,seat),OwnedCardMoveIntent.Discard))).Select(c=>new MouJiangJiCard(c.Id,new(z,seat),c.Kind,c.DisplayName))).ToArray());
  public bool Disabled(int seat,string instance)=>e.GetProgramBooleanState(seat,MouJiangJiRuntime.Yingshi,instance,"disabled");
  public bool Parent(ProgramSkillFrame f)
  {
   if(f.SkillId==MouJiangJiRuntime.Shiju)
   {
    if(f.TriggerId is not null||f.ActivationId!="give-and-use"||f.WindowContext is not null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||History<ProgramSkillStartedEvent>().Count(p=>p.FrameId==f.Id&&p.OwnerSeat==f.OwnerSeat&&p.SkillId==f.SkillId&&p.ActivationId==f.ActivationId)!=1)return false;
    return f.ActivatingSeat is not{}actor?f.ActivationPhase is null:f.ActivationPhase is{}phase&&History<ProgramSharedActivationStartedEvent>().Count(p=>p.FrameId==f.Id&&p.ActorSeat==actor&&p.Source==MouJiangJiRuntime.Source(f)&&p.Phase==phase)==1;
   }
   if(f.TriggerId!="ordinary-trick-target"||f.WindowContext is not{}w||f.ActivationId!=f.TriggerId)return false;var at=e._resolutionStack.FindIndex(x=>x.Id==f.Id);return at>0&&e._resolutionStack[at-1].Id==w.ParentFrameId&&Use(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0,w.OccurrenceIndex),w)is not null;
  }
  public MouJiangJiUse? Use(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
  {
   if(c.SkillId!=MouJiangJiRuntime.Yingshi||w.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||w.OwnerSeat!=c.OwnerSeat||w.CardUse is not{}card||e._resolutionStack.SingleOrDefault(x=>x.Id==w.ParentFrameId)is not ProgramCardTriggerWindowFrame window||window.ParentFrameId!=card.ParentCardUseFrameId||window.CompletedResponseReturn is not null||window.Continuation!=ProgramCardContinuation.FinalizedTrick||window.TrickContinuation is null||e.LifecycleCardUse(window.ParentFrameId)is not{Action:{Type:CardActionType.Use}a}use||a.ActorSeat!=c.OwnerSeat||a.ActionId!=card.CardActionId||window.Action.ActionId!=a.ActionId||use.SourceSeat!=c.OwnerSeat||!IsOrdinaryTrick(use.CardKind)||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count)return null;
   var now=window.Candidates[window.CandidateIndex];if(now.OwnerSeat!=c.OwnerSeat||now.SkillId!=c.SkillId||now.TriggerId!=c.BindingId||now.SkillInstanceId!=c.SkillInstanceId||now.GameplayHash!=c.GameplayHash)return null;
   var targets=use.CardKind==CardKind.BorrowedSword?use.TargetSeats.Where((_,i)=>i%2==0).ToArray():use.TargetSeats.ToArray();return new(use.Id,a.ActionId,window.Id,use.CardKind){Targets=targets};
  }
  public IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f)=>e.MouJjUseOptions(f);
  public CardKind UseKind(int actor,LegalAction action)=>action.PlayedCardKind??e.AdvancedEffectiveHandKind(e._players[actor],e.ApplyProgramUseAppearance(e._players[actor],e.GetAdvancedCard(action.CardId!.Value),action.ConversionSource));
 }
 [ProgramCapability]private sealed class MouJiangJiNative(GameEngine e):IMouJiangJiNative
 {
  public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();
  public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public void Give(ProgramSkillFrame f)
  {
   if(!e.MouJiangJi.Valid(f)||f.MouJiangJi is not{Stage:MouJiangJiStage.GiveChildren,Card:{}card}r||e._cardZones.GetLocation(card.Id)!=card.From)throw new InvalidOperationException("Shiju requires its own committed actual HE entity.");
   if(r.Giver==f.OwnerSeat&&card.From==CardLocation.Hand(r.Giver)){e.ReplaceRuntimeFrame(f.Id,f with{MouJiangJi=r with{Delivered=true}});e.AdvanceEventRulesAndQueueFact(new MouJiangJiSelfCardCommittedEvent(f.Id,card.Id));return;}
   e.MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(MouJiangJiRuntime.GiveReason),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouJiangJi!;if(!e.MouJiangJi.Valid(parent)||moves is not[var m]||m.CardId!=card.Id||m.From!=card.From||m.Sequence<=r.Before||m.To!=CardLocation.Hand(f.OwnerSeat)&&m.To!=CardLocation.OutsideGame)throw new InvalidOperationException("Shiju lost its primary true gift receipt.");e.ReplaceRuntimeFrame(f.Id,parent with{MouJiangJi=paid with{Delivered=m.To==CardLocation.Hand(f.OwnerSeat),After=m.Sequence}});if(m.To==CardLocation.Hand(f.OwnerSeat))e.AdvanceEventRulesAndQueueFact(new MouJiangJiGivenEvent(f.Id,r.Giver,f.OwnerSeat,card.Id,m.Sequence));e.RecordNativeGivenCards(f.Id,r.Giver,f.OwnerSeat,[card.Id],r.Before);});
  }
  public void Use(ProgramSkillFrame f)=>e.IssueMouJjUse(f);public void Repeat(ProgramSkillFrame f)=>e.IssueMouJjRepeat(f);
  public void Discard(ProgramSkillFrame f)
  {
   if(!e.MouJiangJi.Valid(f)||f.MouJiangJi is not{Stage:MouJiangJiStage.DiscardChildren}r||r.Selected.Any(id=>!new MouJiangJiFacts(e).Cards(r.Target,true).Any(c=>c.Id==id)))throw new InvalidOperationException("Yingshi requires its actual target's selected payable HE.");
   e.MoveProgramLogicalDiscard(f,r.Target,r.Selected,new(MouJiangJiRuntime.DiscardReason),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouJiangJi!;if(!e.MouJiangJi.Valid(parent)||moves.Count!=r.Selected.Count||moves.Any(m=>!r.Selected.Contains(m.CardId)||m.From.OwnerSeat!=r.Target||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Yingshi lost its actual native logical discard.");e.ReplaceRuntimeFrame(f.Id,parent with{MouJiangJi=paid with{After=moves.Max(m=>m.Sequence)}});foreach(var m in moves)e.AdvanceEventRulesAndQueueFact(new MouJiangJiDiscardPaidEvent(f.Id,r.Target,m.CardId,m.Sequence));});
  }
  public void Disable(ProgramSkillFrame f){if(!e.MouJiangJi.Valid(f)||f.MouJiangJi is not{Stage:MouJiangJiStage.DiscardChildren}r)throw new InvalidOperationException("Yingshi cannot disable before its issued discard branch returns.");e.SetProgramBooleanState(f.OwnerSeat,MouJiangJiRuntime.Yingshi,f.SkillInstanceId,"disabled",true);e.AdvanceEventRulesAndQueueFact(new MouJiangJiDisabledEvent(f.Id,r.Source,r.Turn));}
 }
 [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying,36300)]private bool MouJjDamage(IDamageAttempt a,long id)
 {if(!a.DamageWasApplied||a is not CardAttackHandle||_resolutionStack.SingleOrDefault(x=>x.Id==id)is not DamageFrame{Amount:>0})return false;foreach(var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.MouJiangJi is{Stage:MouJiangJiStage.NativeUse,Native:{}n}&&n.UseId==a.ResolutionId).ToArray())MouJiangJi.Damage(f);return false;}
}
