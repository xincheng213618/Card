using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule,ProgramCapability]
internal sealed class MouChengYuAnniversaryRuntime(IMouChengYuAnniversaryFacts facts,IMouChengYuAnniversaryNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts):IMouChengYuAnniversaryRuntime
{
 internal const string Shizha="mou:shizha",Gaojian="mou:gaojian";
 private static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal IReadOnlyList<MouCyNameRecordedEvent> Names(int owner,string? instance=null)=>Array.AsReadOnly(facts.History<MouCyNameRecordedEvent>().Where(r=>r.Source.OwnerSeat==owner&&r.Round==facts.Round&&(instance is null||r.Source.SkillInstanceId==instance)&&r.GameplayHash==facts.Program(Shizha)?.GameplayHash&&!facts.History<MouCyNameConsumedEvent>().Any(c=>c.RecordFrameId==r.FrameId)).ToArray());
 [ProgramPipelineHook(ProgramPipelinePhase.IncludeHealthChangeCandidates,36800)]private IReadOnlyList<ProgramTriggerCandidate> Health(HpChangeContext change,IReadOnlyList<ProgramTriggerCandidate> ordinary)=>!facts.Enabled||change.HpBefore==change.HpAfter?ordinary:Array.AsReadOnly(ordinary.Where(c=>c.SkillId!=Shizha||c.BindingId!="hp-change").Concat(facts.Living().SelectMany(s=>facts.Candidates(s,SkillProgramTriggerWindow.AfterHealthChanged)).Where(c=>c.SkillId==Shizha&&c.BindingId=="hp-change")).DistinctBy(c=>(c.OwnerSeat,c.SkillId,c.BindingId,c.SkillInstanceId)).ToArray());
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,36800,SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha,SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha)]private bool CanOffer(ProgramTriggerLegalityContext g)=>g.Candidate.BindingId=="hp-change"?g.Context.Window==SkillProgramTriggerWindow.AfterHealthChanged&&g.Context.HpChange is{Amount:>0}h&&h.Id==g.Context.ParentFrameId:g.Context.Window==SkillProgramTriggerWindow.CardUseCommitted&&facts.Use(g.Context)is{}u&&Names(g.Candidate.OwnerSeat,g.Candidate.SkillInstanceId).Any(r=>r.Name==facts.CardName(u.Action.EffectiveKind));
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouChengYuAnniversary is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!facts.Parent(f))throw new InvalidOperationException("Mou Cheng Yu requires his actual native entry.");
  var r=new MouCyReceipt(Source(f),f.GameplayHash,op,facts.Turn,facts.Round,f.WindowContext?.ParentFrameId??0){Before=facts.Sequence,After=facts.Sequence};
  if(op==SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha)
  {var u=facts.Use(f.WindowContext!)??throw new InvalidOperationException("Shizha requires a true committed native Use.");var record=Names(f.OwnerSeat,f.SkillInstanceId).SingleOrDefault(n=>n.Name==facts.CardName(u.Action.EffectiveKind))??throw new InvalidOperationException("Shizha lost its matching secret name.");r=r with{Stage=MouCyStage.Invalidate,Use=u,Record=record};}
  else r=r with{Stage=op==SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha?MouCyStage.Peek:MouCyStage.Target};
  frames.ReplaceTop(f=f with{MouChengYuAnniversary=r});events.AdvanceFact(new MouCyStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.Round,r.ParentId));
  if(r.Stage==MouCyStage.Invalidate)events.AdvanceFact(new MouCyNameConsumedEvent(f.Id,r.Record!.FrameId,r.Use!.Action.ActionId));
  if(r.Stage==MouCyStage.Peek){var view=native.Peek(f);frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{View=view}});if(view.Count==0){Finish(f);return SkillProgramStepOutcome.AwaitChild;}}
  Publish(f);return SkillProgramStepOutcome.AwaitChoice;
 }
 internal bool Valid(ProgramSkillFrame f)=>f.MouChengYuAnniversary is{}r&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==r.GameplayHash&&f.InstructionIndex==1&&r.Before<=r.After&&r.After<=facts.Sequence&&r.Turn==facts.Turn&&facts.Parent(f)&&facts.History<MouCyStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.Round==r.Round&&e.ParentId==r.ParentId)==1;
 private static PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int value=-1)=>new(new($"mou-cy.{f.Id}.{f.MouChengYuAnniversary!.Stage}.{branch}.{value}"),label,branch=="card"?[value]:[],branch=="target"?[value]:[],new Dictionary<string,string>{{"program-action","mou-cy"},{"branch",branch},{"value",value.ToString(CultureInfo.InvariantCulture)}});
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouChengYuAnniversary!;return r.Stage switch{
   MouCyStage.Peek=>Array.AsReadOnly(r.View.Where(c=>facts.At(c.Id,CardLocation.DrawPile)).Select(c=>Choice(f,"card","获得并记录【"+c.Label+"】",c.Id)).ToArray()),
   MouCyStage.Invalidate=>[Choice(f,"invalidate","令此牌无效"),Choice(f,"decline","不令此牌无效")],
   MouCyStage.Target=>Array.AsReadOnly(facts.Living().Select(s=>Choice(f,"target","令"+facts.Name(s)+"亮牌",s)).ToArray()),
   MouCyStage.RevealChoice=>[Choice(f,"gain",r.RevealCount-1<facts.LivingCount?"获得此牌并再次亮牌":"获得此牌"),Choice(f,"range","攻击范围-"+r.RevealCount)],_=>[]};
 }
 private int Chooser(ProgramSkillFrame f)=>f.MouChengYuAnniversary!.Stage==MouCyStage.RevealChoice?f.MouChengYuAnniversary.Target:f.OwnerSeat;
 private void Publish(ProgramSkillFrame f)
 {
  var choices=Choices(f);if(choices.Count==0||!facts.Alive(Chooser(f))){Finish(f);return;}var seat=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,seat,f.SkillId==Shizha?"识诈：请选择。":"告谏：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=f.SkillId==Shizha,Choices=choices,SkillPrompt=facts.Presentation(f.SkillId)},facts.Human(seat));native.Publish();
 }
 [ProgramAnswer("mou-cy")]private void Answer(PromptChoice choice)
 {
  if(frames.Top is not ProgramSkillFrame{MouChengYuAnniversary:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||!Choices(f).Any(c=>facts.SameChoice(c,choice)))throw new InvalidOperationException("Mou Cheng Yu requires the exact current private choice and owning paid frame.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var value=int.Parse(choice.Parameters["value"],CultureInfo.InvariantCulture);
  switch(choice.Parameters["branch"])
  {
   case"card":var card=r.View.Single(c=>c.Id==value);frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.ObtainChildren,CardId=card.Id,Kind=card.Kind,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Move(f,value,CardLocation.DrawPile,CardLocation.Hand(f.OwnerSeat));Continue(f.Id);break;
   case"invalidate":var proof=new MouCyUseInvalidatedEvent(f.Id,r.Record!.FrameId,r.Source,r.GameplayHash,r.Use!);events.AdvanceFact(proof);native.Invalidate(f,proof);Finish(frames.ActiveProgram(f.Id));break;
   case"decline":Finish(f);break;
   case"target":frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Target=value}});Reveal(f);break;
   case"gain":frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.GainChildren,Again=r.RevealCount-1<facts.LivingCount,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(r.Target,0,null)});if(facts.At(r.CardId,CardLocation.Processing))native.Move(f,r.CardId,CardLocation.Processing,CardLocation.Hand(r.Target));Continue(f.Id);break;
   case"range":events.AdvanceFact(new MouCyRangeIssuedEvent(f.Id,r.Source,r.GameplayHash,r.Target,r.RevealCount,facts.Turn));Discard(f,false);break;
   default:throw new InvalidOperationException("Unknown Mou Cheng Yu choice.");
  }
 }
 private void Reveal(ProgramSkillFrame f)
 {
  var r=f.MouChengYuAnniversary!;frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.RevealChildren,CardId=0,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(r.Target,0,null)});var shown=native.Reveal(f);f=frames.ActiveProgram(f.Id);r=f.MouChengYuAnniversary!;
  if(shown is{}c){r=r with{CardId=c.Id,Kind=c.Kind,RevealCount=r.RevealCount+1,DamageCard=facts.DamageCard(c.Kind)};events.AdvanceFact(new MouCyRevealedEvent(f.Id,c.Id,c.Kind,r.RevealCount,facts.Sequence));}frames.ReplaceTop(f with{MouChengYuAnniversary=r with{After=facts.Sequence}});Continue(f.Id);
 }
 private void Discard(ProgramSkillFrame f,bool damage)
 {
  var r=f.MouChengYuAnniversary!;frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.DiscardChildren,DamageCard=damage,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(r.Target,0,null)});if(facts.At(r.CardId,CardLocation.Processing))native.Move(f,r.CardId,CardLocation.Processing,CardLocation.DiscardPile);Continue(f.Id);
 }
 internal void Moved(long id,IReadOnlyList<CardMovementRecord> moves)
 {
  var f=frames.ActiveProgram(id);if(!Valid(f)||f.MouChengYuAnniversary is not{}r||moves.Count!=1||moves[0].CardId!=r.CardId||moves[0].Sequence<=r.Before)throw new InvalidOperationException("Mou Cheng Yu lost the actual current entity movement.");var m=moves[0];frames.Replace(id,f with{MouChengYuAnniversary=r with{After=m.Sequence,Obtained=r.Stage==MouCyStage.ObtainChildren&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)}});events.AdvanceFact(new MouCyMovedEvent(id,m.CardId,m.From,m.To,m.Sequence));
 }
 internal void Continue(long id)
 {
  if(frames.Top is not ProgramSkillFrame{MouChengYuAnniversary:{}r}f||f.Id!=id)return;if(!Valid(f))throw new InvalidOperationException("Mou Cheng Yu lost its actual paid continuation.");
  if(r.Stage is MouCyStage.Peek or MouCyStage.Invalidate or MouCyStage.Target or MouCyStage.RevealChoice){Publish(f);return;}
  if(f.AttackAttempt is not null)return;if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouChengYuAnniversary!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
  if(r.Stage==MouCyStage.ObtainChildren){if(r.Obtained&&!Names(f.OwnerSeat,f.SkillInstanceId).Any(n=>n.Name==facts.CardName(r.Kind)))events.AdvanceFact(new MouCyNameRecordedEvent(f.Id,r.Source,r.GameplayHash,r.Round,facts.CardName(r.Kind),r.CardId,r.After));Finish(f);return;}
  if(r.Stage==MouCyStage.Damage){Discard(f,false);return;}
  if(facts.Finished||!facts.Alive(r.Target)){Finish(f);return;}
  if(r.Stage==MouCyStage.RevealChildren){if(r.CardId==0){Finish(f);return;}if(r.DamageCard){frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.Damage}});native.Damage(f,r.Target,r.RevealCount);return;}frames.ReplaceTop(f=f with{MouChengYuAnniversary=r with{Stage=MouCyStage.RevealChoice}});Publish(f);return;}
  if(r.Stage==MouCyStage.GainChildren&&r.Again){Reveal(f);return;}
  Finish(f);
 }
 private void Finish(ProgramSkillFrame f){events.AdvanceFact(new MouCyCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouChengYuAnniversary=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,36800)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouChengYuAnniversary:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,36800)]private bool Movement(ProgramSkillFrame f)=>f.MouChengYuAnniversary is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36800)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36800)]private bool Paid(ProgramSkillFrame f)=>f.MouChengYuAnniversary is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36800)]private void Invariant(ProgramSkillFrame f){if(f.MouChengYuAnniversary is not null&&!Valid(f))throw new InvalidOperationException("Invalid Mou Cheng Yu paid source or movement interval.");}
 [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChengYuAnniversaryShizha,SkillProgramEffectOp.ConsumeMouChengYuAnniversaryShizha,SkillProgramEffectOp.ResolveMouChengYuAnniversaryGaojian)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
 [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions,36800)]private void Range(CharacterState actor,List<RuleQueryContribution> contributions)
 {foreach(var g in facts.History<MouCyRangeIssuedEvent>().Where(g=>g.Target==actor.Seat&&g.Amount>0&&!facts.History<TurnEndedEvent>().Any(t=>t.ActorSeat==g.Target&&t.TurnNumber>=g.IssuedTurn)))contributions.Add(new FiniteRuleQueryContribution("issued:mou-gaojian:"+g.FrameId,SkillRuleOperation.Add,-g.Amount));}
}
