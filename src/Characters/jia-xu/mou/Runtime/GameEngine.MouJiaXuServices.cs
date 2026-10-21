namespace CardGame.Core;
public sealed partial class GameEngine
{
 private MouJiaXuRuntime MouJiaXu=>GetRuntimeModule<MouJiaXuRuntime>();
 private bool HasMouJiaXu=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouJiaXuFumou);
 [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo,36500)]private bool MouJxImplicitDrawTwo(ProgramTargetCardWindowQuery q)=>HasMouJiaXu;
 [ProgramCapability]private sealed class MouJiaXuFacts(GameEngine e):IMouJiaXuFacts
 {
  public int Turn=>e._turnNumber;public long Sequence=>e._movementSequence;public bool Finished=>e._winner!=Winner.None;public bool Alive(int s)=>e.IsValidPlayerSeat(s)&&e._players[s].IsAlive;public bool Human(int s)=>e._players[s].IsHuman;public bool ActualPlay(int s)=>Alive(s)&&e._currentSeat==s&&e._phase==TurnPhase.Play&&e._cardUseDebitPhaseInstanceId>0;public int Hp(int s)=>e._players[s].Hp;public int MaximumHp(int s)=>e._players[s].MaxHp;public string Name(int s)=>e._players[s].Name;
  public SkillProgram? Program(string id)=>e._contentRegistry.Skills.GetValueOrDefault(id)?.Program;public IEnumerable<T> History<T>()where T:IGameEvent=>e.ProgramEventHistory<T>();public IReadOnlyList<int> Living()=>Array.AsReadOnly(e._players.Where(p=>p.IsAlive).Select(p=>p.Seat).ToArray());
  public IReadOnlyList<MouJiaXuCard> Hand(int seat,bool discard=false)=>!Alive(seat)?[]:Array.AsReadOnly(e.GetHand(e._players[seat]).Where(c=>!discard||!e.IsSelfHandCategoryDiscardForbidden(seat,c,CardLocation.Hand(seat),OwnedCardMoveIntent.Discard)).Select(c=>new MouJiaXuCard(c.Id,seat,c.DisplayName,e.AdvancedEffectiveHandKind(e._players[seat],c))).ToArray());
  public string FumouInstance(int owner)=>e.GetRuntimeSkillInstanceId(e._players[owner],MouJiaXuRuntime.Fumou);public bool Yin(int owner,string instance)=>e.GetProgramBooleanState(owner,MouJiaXuRuntime.Fumou,instance,"yin");
  public bool Parent(ProgramSkillFrame f)
  {
   if(f.TriggerId is null)return f.WindowContext is null&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&((f.SkillId,f.ActivationId)is(MouJiaXuRuntime.Fumou,"view-show-and-use")or(MouJiaXuRuntime.Sushen,"record-and-enter")or(MouJiaXuRuntime.Rushi,"restore-recorded-values"))&&History<ProgramSkillStartedEvent>().Count(p=>p.FrameId==f.Id&&p.OwnerSeat==f.OwnerSeat&&p.SkillId==f.SkillId&&p.ActivationId==f.ActivationId)==1;
   var at=e._resolutionStack.FindIndex(x=>x.Id==f.Id);return f.SkillId==MouJiaXuRuntime.Fumou&&f.TriggerId=="opening-state"&&f.ActivationId==f.TriggerId&&f.WindowContext is{Window:SkillProgramTriggerWindow.GameStarting}w&&w.OwnerSeat==f.OwnerSeat&&at>0&&e._resolutionStack[at-1]is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.GameStarting}parent&&parent.Id==w.ParentFrameId;
  }
  public IReadOnlyList<LegalAction> UseOptions(ProgramSkillFrame f)=>e.MouJxUseOptions(f);public CardKind UseKind(int actor,LegalAction a)=>a.PlayedCardKind??e.AdvancedEffectiveHandKind(e._players[actor],e.ApplyProgramUseAppearance(e._players[actor],e.GetAdvancedCard(a.CardId!.Value),a.ConversionSource));
 }
 [ProgramCapability]private sealed class MouJiaXuNative(GameEngine e):IMouJiaXuNative
 {
  public void Finish(ProgramSkillFrame f)=>e.FinishProgramSkill(f,true);public void Publish()=>e.AdvanceRulesAndPublishState();
  public bool Drain(ProgramSkillFrame f)=>e.TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||e.TryBeginAdvancedSkillsChanged(f.Id)||e.TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||e.TryBeginCardsMovedProgramWindow(f.Id);
  public void State(ProgramSkillFrame f,bool yin){if(!e.MouJiaXu.Valid(f))throw new InvalidOperationException("Fumou's state requires its own paid source and frozen instance.");e.SetProgramBooleanState(f.OwnerSeat,MouJiaXuRuntime.Fumou,f.MouJiaXu!.FumouInstance,"yin",yin);}
  public void Grant(ProgramSkillFrame f)
  {if(!e.MouJiaXu.Valid(f)||f.MouJiaXu is not{Stage:MouJiaXuStage.GrantChildren,Record:{}record}||record.FrameId!=f.Id||e.ProgramEventHistory<MouJiaXuDerivedGrantedEvent>().Any(g=>g.FrameId==f.Id))throw new InvalidOperationException("Sushen must record real current values before actually granting derived Rushi once.");e.AcquireRuntimeSkills(e._players[f.OwnerSeat],$"sushen:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}",[MouJiaXuRuntime.Rushi]);e.AdvanceEventRulesAndQueueFact(new MouJiaXuDerivedGrantedEvent(f.Id,f.OwnerSeat));}
  public void Reveal(ProgramSkillFrame f)
  {
   if(!e.MouJiaXu.Valid(f)||f.MouJiaXu is not{Stage:MouJiaXuStage.Use or MouJiaXuStage.Recipient}r||r.Frozen.Count>(r.View.Count+1)/2||r.Frozen.Any(id=>!r.View.Any(c=>c.Id==id)||e._cardZones.GetLocation(id)!=CardLocation.Hand(r.Holder)))throw new InvalidOperationException("Fumou may reveal only the exact chosen half of its privately viewed real Hand.");
   if(r.Frozen.Count>0)e.AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id,f.SkillId,f.ActivationId,f.OwnerSeat,"fumou-shown",Array.AsReadOnly(r.Frozen.Select(id=>e.ToSnapshot(e.GetAdvancedCard(id))).ToArray())));foreach(var id in r.Frozen)e.AdvanceEventRulesAndQueueFact(new MouJiaXuShownEvent(f.Id,r.Holder,id));
  }
  public void Transfer(ProgramSkillFrame f)
  {
   if(!e.MouJiaXu.Valid(f)||f.MouJiaXu is not{Stage:MouJiaXuStage.TransferChildren,Yin:false}r||r.Recipient==f.OwnerSeat||r.Recipient==r.Holder||!e._players[r.Recipient].IsAlive)throw new InvalidOperationException("Fumou Yang needs its selected third living recipient and exact shown entities.");var ids=r.Frozen.Where(id=>e._cardZones.GetLocation(id)==CardLocation.Hand(r.Holder)).ToArray();if(ids.Length==0)return;
   e.MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(r.Recipient),new(MouJiaXuRuntime.TransferReason),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouJiaXu!;if(!e.MouJiaXu.Valid(parent)||moves.Count!=ids.Length||moves.Any(m=>!ids.Contains(m.CardId)||m.From!=CardLocation.Hand(r.Holder)||m.Sequence<=r.Before||m.To!=CardLocation.Hand(r.Recipient)&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Fumou lost the primary actual shown-card obtain receipt.");e.ReplaceRuntimeFrame(f.Id,parent with{MouJiaXu=paid with{After=moves.Max(m=>m.Sequence)}});foreach(var m in moves.Where(m=>m.To==CardLocation.Hand(r.Recipient)))e.AdvanceEventRulesAndQueueFact(new MouJiaXuTransferredEvent(f.Id,r.Holder,r.Recipient,m.CardId,m.Sequence));});
  }
  public void Use(ProgramSkillFrame f)=>e.IssueMouJxUse(f);
  public void SetHealth(ProgramSkillFrame f)
  {
   if(!e.MouJiaXu.Valid(f)||f.MouJiaXu is not{Operation:SkillProgramEffectOp.RestoreMouJiaXuRushi,Stage:MouJiaXuStage.HealthChildren,Record:{}record}r||e._players[f.OwnerSeat].Hp!=r.HpBefore||r.HpAfter!=Math.Min(e._players[f.OwnerSeat].MaxHp,record.Hp))throw new InvalidOperationException("Rushi requires the actual recorded HP and current native maximum.");
   var owner=e._players[f.OwnerSeat];owner.Hp=r.HpAfter;var change=e.RecordHpChange(f.Id,f.OwnerSeat,f.OwnerSeat,r.HpBefore,r.HpAfter,HpChangeKind.Setting);e.ReplaceRuntimeFrame(f.Id,f with{MouJiaXu=r with{HpChangeId=change?.Id}});e.AdvanceEventRulesAndQueueFact(new MouJiaXuHpSetEvent(f.Id,record.FrameId,r.HpBefore,r.HpAfter,change?.Id));
  }
  public void Discard(ProgramSkillFrame f)
  {
   if(!e.MouJiaXu.Valid(f)||f.MouJiaXu is not{Stage:MouJiaXuStage.DiscardChildren,Record:not null}r||r.Selected.Any(id=>!new MouJiaXuFacts(e).Hand(f.OwnerSeat,true).Any(c=>c.Id==id)))throw new InvalidOperationException("Rushi requires its selected real discardable Hand entities.");
   e.MoveProgramLogicalDiscard(f,f.OwnerSeat,r.Selected,new(MouJiaXuRuntime.DiscardReason),(_,moves)=>{var parent=e.GetActiveProgramFrame(f.Id);var paid=parent.MouJiaXu!;if(!e.MouJiaXu.Valid(parent)||moves.Count!=r.Selected.Count||moves.Any(m=>!r.Selected.Contains(m.CardId)||m.From!=CardLocation.Hand(f.OwnerSeat)||m.Sequence<=r.Before||m.To!=CardLocation.DiscardPile&&m.To!=CardLocation.OutsideGame))throw new InvalidOperationException("Rushi lost its actual logical Hand discard.");e.ReplaceRuntimeFrame(f.Id,parent with{MouJiaXu=paid with{After=moves.Max(m=>m.Sequence)}});foreach(var m in moves)e.AdvanceEventRulesAndQueueFact(new MouJiaXuDiscardPaidEvent(f.Id,m.CardId,m.Sequence));});
  }
 }
 [ProgramViewContributor(ProgramViewContribution.PrivateCards,36500)]private CardSnapshot[] MouJxPrivateHandView(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer&&f.MouJiaXu is{Stage:MouJiaXuStage.Show}&&MouJiaXu.Valid(f)).SelectMany(f=>f.MouJiaXu!.View.Where(c=>_cardZones.GetLocation(c.Id)==CardLocation.Hand(c.Holder))).DistinctBy(c=>c.Id).Select(c=>ToSnapshot(GetAdvancedCard(c.Id))).ToArray();
}
