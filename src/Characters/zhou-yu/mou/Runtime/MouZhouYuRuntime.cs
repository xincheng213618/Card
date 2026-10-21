using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed class MouZhouYuRuntime(IMouZhouYuFacts facts,IMouZhouYuNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouZhouYuRuntime
{
 internal const string Ronghuo="mou:ronghuo",Yingmou="mou:yingmou",Action="mou-zhou-yu-anniversary",Extra="mou-ronghuo-extra",DiscardReason="skill-program.mou:yingmou.discard";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal IReadOnlyList<MouZhouYuFireGrantedEvent> Grants(int owner)=>facts.History<MouZhouYuFireGrantedEvent>().Where(g=>g.Source.OwnerSeat==owner&&facts.Program(g.Source.SkillId)?.GameplayHash==g.GameplayHash&&!facts.History<MouZhouYuFireConsumedEvent>().Any(c=>c.DamageId==g.DamageId&&c.Source==g.Source)).ToArray();
 internal int Budget(int owner)=>facts.FireSources().Any(s=>s.OwnerSeat==owner)?Grants(owner).Count:0;
 internal int DamageBonus(int actor,CardKind kind,bool genuine)=>genuine&&(kind is CardKind.FireSlash or CardKind.FireAttack)&&facts.FireSources().Any(s=>s.OwnerSeat==actor)?facts.FactionCount:0;
 internal void Consume(long id,long action,int actor,CardKind kind)
 {if(kind is not(CardKind.FireSlash or CardKind.FireAttack)||Budget(actor)==0)return;foreach(var g in Grants(actor))events.AdvanceFact(new MouZhouYuFireConsumedEvent(g.DamageId,g.Source,id,action));}
 internal void Damaged(ProgramSkillFrame f,long damage,int target,int amount)
 {if(!Valid(f)||f.MouZhouYuAnniversary is not{Target:var t}r||t!=target||amount<=0)return;frames.Replace(f.Id,f with{MouZhouYuAnniversary=r with{Damaged=true}});events.AdvanceFact(new MouZhouYuTargetDamagedEvent(f.Id,damage,target,amount));}
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,36000,SkillProgramEffectOp.InitializeMouZhouYuYingmou,SkillProgramEffectOp.ResolveMouZhouYuYingmou,SkillProgramEffectOp.IssueMouZhouYuRonghuo)]
 private bool Qualifies(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>t.Effects.Single().Op==SkillProgramEffectOp.IssueMouZhouYuRonghuo?facts.FireDamage(c,w)is not null:t.Effects.Single().Op==SkillProgramEffectOp.InitializeMouZhouYuYingmou?w.Window==SkillProgramTriggerWindow.GameStarting:
  !facts.History<MouZhouYuActivatedEvent>().Any(e=>e.Owner==c.OwnerSeat&&e.Turn==facts.Turn)&&facts.Use(c,w)is{}u&&u.Action.TargetSeats.Any(s=>s!=c.OwnerSeat&&facts.Alive(s));
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
 {
  var f=frames.ActiveProgram(supplied.Id);
  if(operation==SkillProgramEffectOp.IssueMouZhouYuRonghuo)
  {
   if(f.InstructionIndex!=1||f.SkillId!=Ronghuo||f.WindowContext is not{}fireWindow||facts.FireDamage(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0,fireWindow.OccurrenceIndex),fireWindow)is not{}damage)throw new InvalidOperationException("Ronghuo requires its own exact positive native after-fire-damage occurrence.");
   var source=new CardConversionSource(Ronghuo,"next-fire-use",f.OwnerSeat,f.SkillInstanceId);
   if(!facts.History<MouZhouYuFireGrantedEvent>().Any(g=>g.DamageId==damage&&g.Source==source))events.AdvanceFact(new MouZhouYuFireGrantedEvent(damage,source,f.GameplayHash));
   return SkillProgramStepOutcome.Continue;
  }
  if(f.MouZhouYuAnniversary is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||f.WindowContext is not{}w||!facts.Parent(f)||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash)throw new InvalidOperationException("Yingmou requires its exact genuine completed-Use or opening native parent.");
  var initial=operation==SkillProgramEffectOp.InitializeMouZhouYuYingmou;
  var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0);
  var use=initial?null:facts.Use(c,w);
  if(!initial&&(use is null||facts.History<MouZhouYuActivatedEvent>().Any(e=>e.Owner==f.OwnerSeat&&e.Turn==facts.Turn)))throw new InvalidOperationException("Yingmou cannot reuse another Use or the consumed actual-turn budget.");
  var yin=facts.Yin(f.OwnerSeat,f.SkillInstanceId);var r=new MouZhouYuReceipt(Source(f),f.GameplayHash,operation,facts.Turn,w.ParentFrameId,use?.Action.ActionId??0,yin){Stage=initial?MouZhouYuStage.Initial:MouZhouYuStage.Target,Targets=use?.Action.TargetSeats.Where(s=>s!=f.OwnerSeat&&facts.Alive(s)).Distinct().ToArray()??[],Before=facts.Sequence,After=facts.Sequence};
  frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r});events.AdvanceFact(new MouZhouYuStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.Turn,r.ParentId,r.OriginalAction,yin));
  if(!initial){events.AdvanceFact(new MouZhouYuActivatedEvent(f.Id,f.OwnerSeat,r.Turn,r.OriginalAction,yin));native.State(f,!yin);}
  Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 internal bool Valid(ProgramSkillFrame f)=>f.MouZhouYuAnniversary is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&r.Turn==facts.Turn&&r.ParentId==f.WindowContext?.ParentFrameId&&facts.Parent(f)&&
  Enum.IsDefined(r.Stage)&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&r.Frozen.Distinct().Count()==r.Frozen.Count&&r.Remaining.All(r.Frozen.Contains)&&r.Selected.Distinct().Count()==r.Selected.Count&&
  facts.History<MouZhouYuStartedEvent>().Count(e=>e==new MouZhouYuStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.ParentId,r.OriginalAction,r.Yin))==1;
 private int Chooser(ProgramSkillFrame f)=>f.MouZhouYuAnniversary is{Stage:MouZhouYuStage.OfferUse or MouZhouYuStage.Discard,Actor:>=0}r?r.Actor:f.OwnerSeat;
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int?seat=null,int?card=null)=>new(new ChoiceId($"mou-zhou-yu-anniversary.{f.Id}.{f.MouZhouYuAnniversary!.Stage}.{branch}.{seat}.{card}"),label,card is{}id?[id]:[],seat is{}s?[s]:[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouZhouYuAnniversary!;
  if(r.Stage==MouZhouYuStage.Initial)return[Choice(f,"yang","阳：摸牌并视为使用火攻"),Choice(f,"yin","阴：令手牌最多者使用全部相关手牌")];
  if(r.Stage==MouZhouYuStage.Target)return r.Targets.Where(facts.Alive).Select(s=>Choice(f,"target",facts.Name(s),s)).ToArray();
  if(r.Stage==MouZhouYuStage.MaxHandActor){var seats=facts.Living();var max=seats.Max(facts.HandCount);return seats.Where(s=>facts.HandCount(s)==max).Select(s=>Choice(f,"actor",facts.Name(s),s)).ToArray();}
  if(r.Stage==MouZhouYuStage.OfferUse)
  {
   if(r.Yin)return facts.UseOptions(f).Select((a,i)=>new PromptChoice(new ChoiceId($"mou-zhou-yu-anniversary.{f.Id}.use.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch","use"},{"option",i.ToString(CultureInfo.InvariantCulture)}})).ToArray();
   return facts.FireOptions(f).Select((seats,i)=>new PromptChoice(new ChoiceId($"mou-zhou-yu-anniversary.{f.Id}.fire.{i}"),"视为对"+string.Join("、",seats.Select(facts.Name))+"使用火攻",[],seats,new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch","fire"},{"option",i.ToString(CultureInfo.InvariantCulture)}})).ToArray();
  }
  if(r.Stage!=MouZhouYuStage.Discard)return[];
  var n=Math.Min(Math.Max(0,facts.HandCount(r.Actor)-facts.HandCount(f.OwnerSeat)),facts.DiscardableHand(r.Actor).Count);
  var choices=facts.DiscardableHand(r.Actor).Where(c=>r.Selected.Contains(c.Id)||r.Selected.Count<n).Select(c=>Choice(f,r.Selected.Contains(c.Id)?"remove":"add",(r.Selected.Contains(c.Id)?"取消":"选择")+c.Label,card:c.Id)).ToList();
  if(r.Selected.Count==n)choices.Add(Choice(f,"done","确认弃置"+n+"张手牌"));return choices;
 }
 private void Publish(ProgramSkillFrame f)
 {
  var choices=Choices(f);var chooser=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,chooser,"英谋："+(f.MouZhouYuAnniversary!.Stage==MouZhouYuStage.Initial?"选择初始阴阳状态。":"结算当前转换项。"),choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(chooser));native.Publish();
 }
 [ProgramAnswer(Action)] private void Answer(PromptChoice chosen)
 {
  if(frames.Top is not ProgramSkillFrame{MouZhouYuAnniversary:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||Choices(f).SingleOrDefault(c=>c.Id==chosen.Id)is not{}c||!c.Cards.SequenceEqual(chosen.Cards)||!c.Targets.SequenceEqual(chosen.Targets)||c.Parameters.Count!=chosen.Parameters.Count||c.Parameters.Any(x=>chosen.Parameters.GetValueOrDefault(x.Key)!=x.Value))throw new InvalidOperationException("Yingmou requires the current private actor's exact native choice.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=chosen.Parameters["branch"];
  if(r.Stage==MouZhouYuStage.Initial){native.State(f,branch=="yin");Complete(f);return;}
  if(r.Stage==MouZhouYuStage.Target)
  {
   var target=chosen.Targets.Single();frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r=r with{Target=target,Actor=f.OwnerSeat,Stage=r.Yin?MouZhouYuStage.MaxHandActor:MouZhouYuStage.Drawing,Requested=Math.Min(5,Math.Max(0,facts.HandCount(target)-facts.HandCount(f.OwnerSeat)))}});
   if(!r.Yin&&r.Requested>0){draws.BeginOperationProgramDraw(f,f.OwnerSeat,r.Requested,new("skill-program.mou:yingmou.draw"));return;}
   if(!r.Yin)frames.ReplaceTop(f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.OfferUse}});Continue(f.Id);return;
  }
  if(r.Stage==MouZhouYuStage.MaxHandActor)
  {
   var actor=chosen.Targets.Single();var related=facts.Hand(actor).Where(c=>c.Related).Select(c=>c.Id).ToArray();frames.ReplaceTop(f with{MouZhouYuAnniversary=r with{Actor=actor,Frozen=related,Remaining=related,Stage=related.Length==0?MouZhouYuStage.Discard:MouZhouYuStage.OfferUse}});Continue(f.Id);return;
  }
  if(r.Stage==MouZhouYuStage.OfferUse)
  {
   var i=int.Parse(chosen.Parameters["option"],CultureInfo.InvariantCulture);var action=r.Yin?facts.UseOptions(f)[i]:null;var seats=r.Yin?chosen.Targets:facts.FireOptions(f)[i];
   var kind=r.Yin?(action!.PlayedCardKind??facts.Hand(r.Actor).Single(x=>x.Id==action.CardId).Kind):CardKind.FireAttack;
   var paid=new MouZhouYuNativeUse(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,r.Turn,r.Actor,action?.CardId??0,kind,action,0,0){Targets=seats};
   frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.IssuingUse,Native=paid}});native.Use(f);return;
  }
  if(branch!="done"){var id=chosen.Cards.Single();frames.ReplaceTop(f with{MouZhouYuAnniversary=r with{Selected=branch=="add"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});Continue(f.Id);return;}
  frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.DiscardChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(r.Selected.Count>0)native.Discard(f);Continue(f.Id);
 }
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouZhouYuAnniversary:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Yingmou lost its actual native parent or paid child receipt.");
   if(facts.Finished){Complete(f);return;}
   if(r.Stage is MouZhouYuStage.Initial or MouZhouYuStage.Target or MouZhouYuStage.MaxHandActor){if(Choices(f).Count==0){Complete(f);return;}Publish(f);return;}
   if(!facts.Alive(r.Target)){Complete(f);return;}
   if(r.Stage==MouZhouYuStage.Discard){var allowed=facts.DiscardableHand(r.Actor).Select(c=>c.Id).ToHashSet();var need=Math.Min(Math.Max(0,facts.HandCount(r.Actor)-facts.HandCount(f.OwnerSeat)),allowed.Count);var selected=r.Selected.Where(allowed.Contains).Take(need).ToArray();if(!selected.SequenceEqual(r.Selected))frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r=r with{Selected=selected}});if(need==0){FinalDamage(f);return;}Publish(f);return;}
   if(r.Stage==MouZhouYuStage.OfferUse){if(Choices(f).Count==0){FinalDamage(f);return;}Publish(f);return;}
   if(r.Stage is MouZhouYuStage.NativeUse or MouZhouYuStage.IssuingUse or MouZhouYuStage.Drawing)return;
   if(r.Stage==MouZhouYuStage.ReturnedUse){var paid=r.Native!;frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r=r with{Native=null,Remaining=r.Remaining.Where(c=>c!=paid.CardId).ToArray(),Stage=MouZhouYuStage.OfferUse}});if(!r.Yin){FinalDamage(f);return;}continue;}
   if(r.Stage is MouZhouYuStage.DrawChildren or MouZhouYuStage.DiscardChildren or MouZhouYuStage.DamageChildren){if(f.AttackAttempt is not null||native.Drain(f))return;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});if(r.Stage==MouZhouYuStage.DrawChildren){frames.ReplaceTop(f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.OfferUse}});continue;}if(r.Stage==MouZhouYuStage.DiscardChildren){FinalDamage(f);return;}Complete(f);return;}
  }
 }
 private void FinalDamage(ProgramSkillFrame f)
 {var r=f.MouZhouYuAnniversary!;if(r.Damaged||!facts.Alive(r.Target)||!facts.Alive(f.OwnerSeat)){Complete(f);return;}frames.ReplaceTop(f=f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.DamageChildren}});native.Damage(f);Continue(f.Id);}
 internal static bool SameUse(MouZhouYuNativeUse a,MouZhouYuNativeUse b)=>(a.FrameId,a.Instruction,a.Source,a.GameplayHash,a.Turn,a.Actor,a.CardId,a.Kind,a.UseId,a.ActionId)==(b.FrameId,b.Instruction,b.Source,b.GameplayHash,b.Turn,b.Actor,b.CardId,b.Kind,b.UseId,b.ActionId)&&a.Targets.SequenceEqual(b.Targets)&&SameAction(a.Action,b.Action);
 private static bool SameAction(LegalAction? a,LegalAction? b)
 {
  if(a is null||b is null)return a is null&&b is null;
  return (a.Kind,a.CardId,a.TargetSeat,a.PlayedCardKind,a.TargetCardId,a.ConversionSource,a.ProgramSkillId,a.ProgramActivationId,a.ProgramSkillOwnerSeat,a.NativePublicPileSource,a.TargetHandPreselection,a.MinCardCount,a.MaxCardCount,a.MinTargetCount,a.MaxTargetCount)==
   (b.Kind,b.CardId,b.TargetSeat,b.PlayedCardKind,b.TargetCardId,b.ConversionSource,b.ProgramSkillId,b.ProgramActivationId,b.ProgramSkillOwnerSeat,b.NativePublicPileSource,b.TargetHandPreselection,b.MinCardCount,b.MaxCardCount,b.MinTargetCount,b.MaxTargetCount)&&
   a.TargetSeats.SequenceEqual(b.TargetSeats)&&(a.AdditionalConversionSources??[]).SequenceEqual(b.AdditionalConversionSources??[]);
 }
 internal void Returned(MouZhouYuNativeUse use,bool activeAttack)
 {var f=frames.ActiveProgram(use.FrameId);if(!Valid(f)||f.MouZhouYuAnniversary is not{Native:{}r}p||!SameUse(r,use))throw new InvalidOperationException("Yingmou requires its own once-completed native Use.");frames.Replace(f.Id,f with{MouZhouYuAnniversary=p with{Stage=MouZhouYuStage.ReturnedUse}});events.AdvanceFact(new MouZhouYuUseReturnedEvent(f.Id,use.UseId,use.ActionId));if(!activeAttack)frames.AdvanceProgram(f.Id);}
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhouYuYingmou)] private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
 {if(!Valid(f)||f.MouZhouYuAnniversary is not{Stage:MouZhouYuStage.Drawing,Draw:null}r||outcome.RequestedCount!=r.Requested||outcome.RecipientSeat!=f.OwnerSeat)throw new InvalidOperationException("Yingmou draw requires its real issued Yang amount.");frames.Replace(f.Id,f with{MouZhouYuAnniversary=r with{Stage=MouZhouYuStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouZhouYuDrawReturnedEvent(f.Id,r.Requested,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);}
 private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouZhouYuCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouZhouYuAnniversary=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,36000)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouZhouYuAnniversary:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,36000)]private bool Movement(ProgramSkillFrame f)=>f.MouZhouYuAnniversary is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36000)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36000)]private bool Paid(ProgramSkillFrame f)=>f.MouZhouYuAnniversary is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36000)]private void Invariant(ProgramSkillFrame f){if(f.MouZhouYuAnniversary is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Yingmou receipt.");}
 [ProgramAiChoice(SkillProgramEffectOp.InitializeMouZhouYuYingmou,SkillProgramEffectOp.ResolveMouZhouYuYingmou)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.First();
}
