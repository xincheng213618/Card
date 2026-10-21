using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouJiangJiRuntime(IMouJiangJiFacts facts,IMouJiangJiNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouJiangJiRuntime
{
 internal const string Shiju="mou:shiju",Yingshi="mou:yingshi",Action="mou-jiang-ji-anniversary",GiveReason="skill-program.mou:shiju.give",DiscardReason="skill-program.mou:yingshi.discard";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal bool Spent(int owner,int actor)=>facts.History<MouJiangJiStartedEvent>().Any(e=>e.Operation==SkillProgramEffectOp.ResolveMouJiangJiShiju&&e.Source.OwnerSeat==owner&&e.Giver==actor&&e.Turn==facts.Turn&&e.PhaseId==facts.PhaseId);
 internal IEnumerable<LegalAction> ForeignActions(int actor)=>facts.ShijuOwners(actor).Where(owner=>owner!=actor&&!Spent(owner,actor)).Select(owner=>new LegalAction(LegalActionKind.UseProgramSkill,null,null,"交给"+facts.Name(owner)+"一张牌并发动势举"){ProgramSkillId=Shiju,ProgramActivationId="give-and-use",ProgramSkillOwnerSeat=owner,ProgramAiHint=new(2,0,0,0,0,0,true,false)});
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts,36300,SkillProgramEffectOp.ResolveMouJiangJiShiju)]private bool CanActivate(ProgramActivationLegalityContext c)=>facts.ActualPlay(c.OwnerSeat)&&facts.Cards(c.OwnerSeat,false).Count>0&&!Spent(c.OwnerSeat,c.OwnerSeat);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,36300,SkillProgramEffectOp.ResolveMouJiangJiYingshi)]private bool CanTrigger(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>!facts.Disabled(c.OwnerSeat,c.SkillInstanceId)&&facts.Use(c,w)is{}u&&u.Targets.Any(facts.Alive)&&!facts.History<MouJiangJiStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Operation==SkillProgramEffectOp.ResolveMouJiangJiYingshi&&e.ActionId==u.ActionId);
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouJiangJi is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash||!facts.Parent(f))throw new InvalidOperationException("Mou Jiang Ji requires the exact actual Play issuer or original finalized native Trick.");
  var giving=operation==SkillProgramEffectOp.ResolveMouJiangJiShiju;var giver=f.ActivatingSeat??f.OwnerSeat;
  var use=giving?null:facts.Use(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0,f.WindowContext!.OccurrenceIndex),f.WindowContext!);
  if(giving?(f.SkillId!=Shiju||!facts.ActualPlay(giver)||Spent(f.OwnerSeat,giver)):(f.SkillId!=Yingshi||use is null))throw new InvalidOperationException("Mou Jiang Ji lost his live issued source or native target contract.");
  var r=new MouJiangJiReceipt(Source(f),f.GameplayHash,operation,facts.Turn,giver,facts.PhaseId,use?.WindowId??0,use?.UseId??0,use?.ActionId??0){Stage=giving?MouJiangJiStage.Give:MouJiangJiStage.YingshiTarget,Before=facts.Sequence,After=facts.Sequence,Targets=use?.Targets??[]};
  frames.ReplaceTop(f=f with{MouJiangJi=r});events.AdvanceFact(new MouJiangJiStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.Turn,r.Giver,r.PhaseId,r.WindowId,r.UseId,r.ActionId));Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 internal bool Valid(ProgramSkillFrame f)=>f.MouJiangJi is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&facts.Parent(f)&&r.Turn==facts.Turn&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&Enum.IsDefined(r.Stage)&&r.DrawCursor is>=0 and<=2&&r.Selected.Distinct().Count()==r.Selected.Count&&facts.History<MouJiangJiStartedEvent>().Count(e=>e==new MouJiangJiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.Giver,r.PhaseId,r.WindowId,r.UseId,r.ActionId))==1;
 private int Chooser(ProgramSkillFrame f)=>f.MouJiangJi is{Stage:MouJiangJiStage.Give}g?g.Giver:f.MouJiangJi is{Stage:MouJiangJiStage.YingshiChoice or MouJiangJiStage.Discard}t?t.Target:f.OwnerSeat;
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int?card=null,int?target=null)=>new(new ChoiceId($"mou-jiang-ji-anniversary.{f.Id}.{f.MouJiangJi!.Stage}.{branch}.{card}.{target}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
 private int Need(ProgramSkillFrame f)=>Math.Min(f.MouJiangJi!.Requested,facts.Cards(f.MouJiangJi.Target,true).Count);
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouJiangJi!;
  if(r.Stage==MouJiangJiStage.Give)return facts.Cards(r.Giver,false).Select(c=>Choice(f,"give",c.Label,c.Id)).ToArray();
  if(r.Stage==MouJiangJiStage.Use)return facts.UseOptions(f).Select((a,i)=>new PromptChoice(new ChoiceId($"mou-jiang-ji-anniversary.{f.Id}.use.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch","use"},{"option",i.ToString(CultureInfo.InvariantCulture)}})).Append(Choice(f,"decline","不使用交给的此牌")).ToArray();
  if(r.Stage==MouJiangJiStage.YingshiTarget)return r.Targets.Where(facts.Alive).Select(t=>Choice(f,"target",facts.Name(t),target:t)).ToArray();
  if(r.Stage==MouJiangJiStage.YingshiChoice)return[Choice(f,"repeat","此锦囊对我额外结算一次"),Choice(f,"discard","弃置"+(facts.EquipmentCount(f.OwnerSeat)+1)+"张牌，然后应时本回合失效")];
  if(r.Stage!=MouJiangJiStage.Discard)return[];
  var need=Need(f);var choices=facts.Cards(r.Target,true).Where(c=>r.Selected.Contains(c.Id)||r.Selected.Count<need).Select(c=>Choice(f,r.Selected.Contains(c.Id)?"remove":"add",(r.Selected.Contains(c.Id)?"取消":"选择")+c.Label,c.Id)).ToList();if(r.Selected.Count==need)choices.Add(Choice(f,"done","确认弃置"+need+"张牌"));return choices;
 }
 private void Publish(ProgramSkillFrame f)
 {
  var choices=Choices(f);var chooser=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
  prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,chooser,f.MouJiangJi!.Operation==SkillProgramEffectOp.ResolveMouJiangJiShiju?"势举：交给准确实体后，可以真正使用此牌。":"应时：由本次选中目标选择额外结算，或弃牌令技能本回合失效。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(chooser));native.Publish();
 }
 [ProgramAnswer(Action)]private void Answer(PromptChoice choice)
 {
  if(frames.Top is not ProgramSkillFrame{MouJiangJi:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||Choices(f).SingleOrDefault(c=>c.Id==choice.Id)is not{}canonical||!canonical.Cards.SequenceEqual(choice.Cards)||!canonical.Targets.SequenceEqual(choice.Targets)||canonical.Parameters.Count!=choice.Parameters.Count||canonical.Parameters.Any(p=>choice.Parameters.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("Mou Jiang Ji requires the actual chooser's exact published private choice.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=choice.Parameters["branch"];
  if(r.Stage==MouJiangJiStage.Give)
  {
   var card=facts.Cards(r.Giver,false).Single(c=>c.Id==choice.Cards.Single());events.AdvanceFact(new MouJiangJiShijuCommittedEvent(f.Id,f.OwnerSeat,r.Giver,r.Turn,r.PhaseId,card.Id,EquipmentCatalog.IsEquipment(card.Kind)));
   frames.ReplaceTop(f=f with{MouJiangJi=r with{Stage=MouJiangJiStage.GiveChildren,Card=card,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Give(f);Continue(f.Id);return;
  }
  if(r.Stage==MouJiangJiStage.Use)
  {
   if(branch=="decline"){frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.AfterUse}});Continue(f.Id);return;}
   var action=facts.UseOptions(f)[int.Parse(choice.Parameters["option"],CultureInfo.InvariantCulture)];var n=new MouJiangJiNativeUse(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,r.Turn,f.OwnerSeat,r.Card!.Id,facts.UseKind(f.OwnerSeat,action),action,0,0){Targets=action.TargetSeats};frames.ReplaceTop(f=f with{MouJiangJi=r with{Stage=MouJiangJiStage.IssuingUse,Native=n}});native.Use(f);return;
  }
  if(r.Stage==MouJiangJiStage.YingshiTarget){frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.YingshiChoice,Target=choice.Targets.Single()}});Continue(f.Id);return;}
  if(r.Stage==MouJiangJiStage.YingshiChoice)
  {
   if(branch=="repeat"){native.Repeat(f);Complete(f);return;}
   frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.Discard,Requested=facts.EquipmentCount(f.OwnerSeat)+1}});Continue(f.Id);return;
  }
  if(branch!="done"){var id=choice.Cards.Single();frames.ReplaceTop(f with{MouJiangJi=r with{Selected=branch=="add"?r.Selected.Append(id).ToArray():r.Selected.Where(c=>c!=id).ToArray()}});Continue(f.Id);return;}
  StartDiscard(f);
 }
 private void StartDiscard(ProgramSkillFrame f){var r=f.MouJiangJi!;frames.ReplaceTop(f=f with{MouJiangJi=r with{Stage=MouJiangJiStage.DiscardChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(r.Selected.Count>0)native.Discard(f);Continue(f.Id);}
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouJiangJi:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Mou Jiang Ji lost his paid frame, source, native Use or physical child proof.");if(facts.Finished){Complete(f);return;}
   if(r.Stage is MouJiangJiStage.Give or MouJiangJiStage.Use or MouJiangJiStage.YingshiTarget or MouJiangJiStage.YingshiChoice)
   {if(Choices(f).Count==0||!facts.Alive(Chooser(f))){if(r.Stage==MouJiangJiStage.Use){frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.AfterUse}});continue;}Complete(f);return;}Publish(f);return;}
   if(r.Stage==MouJiangJiStage.Discard){var allowed=facts.Cards(r.Target,true).Select(c=>c.Id).ToHashSet();var selected=r.Selected.Where(allowed.Contains).Take(Need(f)).ToArray();if(!selected.SequenceEqual(r.Selected))frames.ReplaceTop(f=f with{MouJiangJi=r=r with{Selected=selected}});if(Need(f)==0){StartDiscard(f);return;}Publish(f);return;}
   if(r.Stage is MouJiangJiStage.IssuingUse or MouJiangJiStage.NativeUse or MouJiangJiStage.Drawing)return;
   if(r.Stage==MouJiangJiStage.ReturnedUse){frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.AfterUse,Native=null}});continue;}
   if(r.Stage is MouJiangJiStage.GiveChildren or MouJiangJiStage.DiscardChildren or MouJiangJiStage.DrawChildren)
   {
    if(native.Drain(f))return;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
    if(r.Stage==MouJiangJiStage.GiveChildren){frames.ReplaceTop(f with{MouJiangJi=r with{Stage=r.Delivered?MouJiangJiStage.Use:MouJiangJiStage.AfterUse}});continue;}
    if(r.Stage==MouJiangJiStage.DiscardChildren){native.Disable(f);Complete(f);return;}
    frames.ReplaceTop(f with{MouJiangJi=r with{Stage=MouJiangJiStage.AfterUse,DrawCursor=r.DrawCursor+1,Draw=null}});continue;
   }
   if(r.Stage==MouJiangJiStage.AfterUse)
   {
    if(!facts.History<MouJiangJiRangeGrantedEvent>().Any(e=>e.FrameId==f.Id))events.AdvanceFact(new MouJiangJiRangeGrantedEvent(f.Id,r.Source,r.GameplayHash,r.Giver,r.Turn,facts.EquipmentCount(f.OwnerSeat)));
    if(r.Card is null||!EquipmentCatalog.IsEquipment(r.Card.Kind)&&!r.Damaged||r.DrawCursor>=2){Complete(f);return;}
    var recipient=r.DrawCursor==0?f.OwnerSeat:r.Giver;if(!facts.Alive(recipient)){frames.ReplaceTop(f with{MouJiangJi=r with{DrawCursor=r.DrawCursor+1}});continue;}
    frames.ReplaceTop(f=f with{MouJiangJi=r with{Stage=MouJiangJiStage.Drawing}});draws.BeginOperationProgramDraw(f,recipient,2,new("skill-program.mou:shiju.draw"));return;
   }
  }
 }
 internal void Damage(ProgramSkillFrame f){if(Valid(f)&&f.MouJiangJi is{Stage:MouJiangJiStage.NativeUse,Damaged:false}r)frames.Replace(f.Id,f with{MouJiangJi=r with{Damaged=true}});}
 internal void Returned(MouJiangJiNativeUse n,bool activeAttack)
 {if(frames.Find(n.FrameId)is not ProgramSkillFrame{MouJiangJi:{Native:{}current}r}f||!Valid(f)||!SameUse(current,n))throw new InvalidOperationException("Shiju requires the exact issued card's once-native return.");frames.Replace(f.Id,f with{MouJiangJi=r with{Stage=MouJiangJiStage.ReturnedUse}});events.AdvanceFact(new MouJiangJiUseReturnedEvent(f.Id,n.UseId,n.ActionId,r.Damaged));if(!activeAttack)frames.AdvanceProgram(f.Id);}
 internal static bool SameUse(MouJiangJiNativeUse a,MouJiangJiNativeUse b)=>(a.FrameId,a.Instruction,a.Source,a.GameplayHash,a.Turn,a.Actor,a.CardId,a.Kind,a.UseId,a.ActionId)==(b.FrameId,b.Instruction,b.Source,b.GameplayHash,b.Turn,b.Actor,b.CardId,b.Kind,b.UseId,b.ActionId)&&a.Targets.SequenceEqual(b.Targets)&&a.Action.Kind==b.Action.Kind&&a.Action.CardId==b.Action.CardId&&a.Action.PlayedCardKind==b.Action.PlayedCardKind&&a.Action.TargetCardId==b.Action.TargetCardId&&a.Action.ConversionSource==b.Action.ConversionSource&&a.Action.TargetSeats.SequenceEqual(b.Action.TargetSeats)&&(a.Action.AdditionalConversionSources??[]).SequenceEqual(b.Action.AdditionalConversionSources??[]);
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouJiangJiShiju)]private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
 {if(!Valid(f)||f.MouJiangJi is not{Stage:MouJiangJiStage.Drawing,Draw:null}r||outcome.RequestedCount!=2||outcome.RecipientSeat!=(r.DrawCursor==0?f.OwnerSeat:r.Giver))throw new InvalidOperationException("Shiju draw requires its exact next identity, including both separate self draws.");frames.Replace(f.Id,f with{MouJiangJi=r with{Stage=MouJiangJiStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouJiangJiDrawReturnedEvent(f.Id,r.DrawCursor,outcome.RecipientSeat,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);}
 private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouJiangJiCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouJiangJi=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,36300)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouJiangJi:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,36300)]private bool Movement(ProgramSkillFrame f)=>f.MouJiangJi is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36300)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36300)]private bool Paid(ProgramSkillFrame f)=>f.MouJiangJi is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36300)]private void Invariant(ProgramSkillFrame f){if(f.MouJiangJi is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Mou Jiang Ji receipt.");}
 [ProgramAiChoice(SkillProgramEffectOp.ResolveMouJiangJiShiju,SkillProgramEffectOp.ResolveMouJiangJiYingshi)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="done")??p.Choices.First();
 [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions,36300)]private void Range(CharacterState actor,List<RuleQueryContribution> contributions)
 {foreach(var g in facts.History<MouJiangJiRangeGrantedEvent>().Where(g=>g.Recipient==actor.Seat&&g.Turn==facts.Turn&&g.Amount>0&&!facts.History<TurnEndedEvent>().Any(e=>e.TurnNumber==g.Turn)))contributions.Add(new FiniteRuleQueryContribution("issued:mou-shiju:"+g.FrameId,SkillRuleOperation.Add,g.Amount));}
}
