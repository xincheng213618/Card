using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouDongChengRuntime(IMouDongChengFacts facts,IMouDongChengNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IMouDcLightningFacts lightning,IMouDcLightningNative delayed):IMouDongChengRuntime
{
 internal const string Baojia="mou:baojia",Douwei="mou:douwei",Action="mou-dong-cheng-anniversary";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal bool Valid(ProgramSkillFrame f)=>f.MouDongCheng is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&r.Turn==facts.Turn&&facts.Parent(f)&&Enum.IsDefined(r.Stage)&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&r.Targets.Distinct().Count()==r.Targets.Count&&r.Dying.Distinct().Count()==r.Dying.Count&&facts.History<MouDcStartedEvent>().Count(e=>e==new MouDcStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.ParentId))==1;
 internal MouDcOccurrence ObserveDamage(long id,long attack,long use,int card,int target)
 {var first=!facts.History<MouDcDamageSeenEvent>().Any(e=>e.Occurrence.Turn==facts.Turn&&e.Occurrence.Target==target);var occurrence=new MouDcOccurrence(id,facts.Turn,attack,use,card,target,first);events.AdvanceFact(new MouDcDamageSeenEvent(occurrence));return occurrence;}
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,37400,SkillProgramEffectOp.ResolveMouDongChengBaojia,SkillProgramEffectOp.ClaimMouDongChengBaojia)]
 private bool Qualifies(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>t.Effects[0].Op==SkillProgramEffectOp.ResolveMouDongChengBaojia?facts.Before(c,w)is not null:facts.ClaimUse(c,w)>0;
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouDongCheng is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash||!facts.Parent(f))throw new InvalidOperationException("Mou Dong Cheng requires his actual opening, first native card damage, issued completed-use claim or own Play activation.");
  var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId??f.ActivationId,f.SkillInstanceId,f.GameplayHash,0);var occurrence=op==SkillProgramEffectOp.ResolveMouDongChengBaojia?facts.Before(c,f.WindowContext!):null;var use=op==SkillProgramEffectOp.ClaimMouDongChengBaojia?facts.ClaimUse(c,f.WindowContext!):0;
  if(op==SkillProgramEffectOp.ResolveMouDongChengBaojia&&occurrence is null||op==SkillProgramEffectOp.ClaimMouDongChengBaojia&&use<=0||op==SkillProgramEffectOp.ResolveMouDongChengDouwei&&!facts.MayActivate(f.OwnerSeat))throw new InvalidOperationException("Mou Dong Cheng lost his qualifying payable native request.");
  var stage=op switch{SkillProgramEffectOp.InitializeMouDongChengBaojia=>MouDongChengStage.Opening,SkillProgramEffectOp.ResolveMouDongChengBaojia=>MouDongChengStage.Slot,SkillProgramEffectOp.ClaimMouDongChengBaojia=>MouDongChengStage.ClaimChildren,_=>MouDongChengStage.Cost};
  var r=new MouDongChengReceipt(Source(f),f.GameplayHash,op,facts.Turn,f.WindowContext?.ParentFrameId??0){Stage=stage,Occurrence=occurrence,UseId=use,Before=facts.Sequence,After=facts.Sequence};frames.ReplaceTop(f=f with{MouDongCheng=r});events.AdvanceFact(new MouDcStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.ParentId));
  if(stage==MouDongChengStage.ClaimChildren){frames.ReplaceTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Claim(f);}Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string text,int? card=null,int? target=null,EquipmentSlot? slot=null)=>new(new ChoiceId($"mou-dong-cheng.{f.Id}.{f.MouDongCheng!.Stage}.{branch}.{card}.{target}.{slot}"),text,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch},{"slot",slot?.ToString()??""}});
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouDongCheng!;if(r.Stage==MouDongChengStage.Opening)return facts.Living.Where(s=>s!=f.OwnerSeat).Select(s=>Choice(f,"link","保驾：选择"+facts.Name(s),target:s)).ToArray();
  if(r.Stage is MouDongChengStage.Slot or MouDongChengStage.Restore)return facts.Slots(f.OwnerSeat,r.Stage==MouDongChengStage.Restore).Select(s=>Choice(f,"slot",(r.Stage==MouDongChengStage.Restore?"恢复":"废弃")+HuoJunSlotLabel(s),slot:s)).ToArray();
  if(r.Stage==MouDongChengStage.Cost)return facts.Costs(f.OwnerSeat).Select(c=>Choice(f,"cost","斗围：弃置"+c.Label,c.Id)).ToArray();
  if(r.Stage==MouDongChengStage.Targets)return facts.Targets(f,r.Kind).Select(s=>Choice(f,r.Targets.Contains(s)?"remove":"add",(r.Targets.Contains(s)?"取消":"选择")+facts.Name(s),target:s)).Append(Choice(f,"done","弃置所选牌，对选中的"+r.Targets.Count+"名角色视为使用")).ToArray();return[];
 }
 private static string HuoJunSlotLabel(EquipmentSlot s)=>s switch{EquipmentSlot.Weapon=>"武器栏",EquipmentSlot.Armor=>"防具栏",EquipmentSlot.OffensiveHorse=>"进攻坐骑栏",EquipmentSlot.DefensiveHorse=>"防御坐骑栏",_=>"宝物栏"};
 private static string PromptText(MouDongChengStage stage)=>stage switch
 {MouDongChengStage.Opening=>"选择一名其他角色。",MouDongChengStage.Slot=>"选择要废弃的装备栏，防止此次伤害。",MouDongChengStage.Cost=>"弃置一张伤害牌。",MouDongChengStage.Targets=>"选择攻击范围内包含你的其他角色。",MouDongChengStage.Restore=>"选择要恢复的装备栏。",_=>throw new InvalidOperationException("Dong Cheng has no choice at this resolution stage.")};
 private void Prompt(ProgramSkillFrame f)
 {var choices=Choices(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,f.OwnerSeat,PromptText(f.MouDongCheng!.Stage),choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=f.MouDongCheng.Stage==MouDongChengStage.Cost,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(f.OwnerSeat));native.Publish();}
 [ProgramAnswer(Action)]private void Answer(PromptChoice answer)
 {
  if(frames.Top is not ProgramSkillFrame{MouDongCheng:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=f.OwnerSeat||Choices(f).SingleOrDefault(c=>c.Id==answer.Id)is not{}c||!c.Cards.SequenceEqual(answer.Cards)||!c.Targets.SequenceEqual(answer.Targets)||c.Parameters.Count!=answer.Parameters.Count||c.Parameters.Any(p=>answer.Parameters.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("Mou Dong Cheng requires his exact current legal target, real cost or live equipment slot.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=c.Parameters["branch"];
  if(r.Stage==MouDongChengStage.Opening){native.Link(f,c.Targets.Single());Complete(f);return;}
  if(r.Stage==MouDongChengStage.Cost){var cost=facts.Costs(f.OwnerSeat).Single(x=>x.Id==c.Cards.Single());frames.ReplaceTop(f with{MouDongCheng=r with{Stage=MouDongChengStage.Targets,CardId=cost.Id,Kind=cost.Kind,Targets=[]}});Continue(f.Id);return;}
  if(r.Stage is MouDongChengStage.Slot or MouDongChengStage.Restore)
  {var restoring=r.Stage==MouDongChengStage.Restore;frames.ReplaceTop(f=f with{MouDongCheng=r with{Slot=Enum.Parse<EquipmentSlot>(c.Parameters["slot"]),Stage=restoring?MouDongChengStage.RestoreChildren:MouDongChengStage.SlotChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(restoring)native.Restore(f);else native.Protect(f);Continue(f.Id);return;}
  if(branch!="done"){var target=c.Targets.Single();frames.ReplaceTop(f with{MouDongCheng=r with{Targets=branch=="add"?r.Targets.Append(target).ToArray():r.Targets.Where(s=>s!=target).ToArray()}});Continue(f.Id);return;}
  if(!facts.Costs(f.OwnerSeat).Any(x=>x.Id==r.CardId&&x.Kind==r.Kind))throw new InvalidOperationException("Douwei cannot pay an expired or no-longer-discardable physical damage card.");
  frames.ReplaceTop(f=f with{MouDongCheng=r with{Stage=MouDongChengStage.CostChildren,Before=facts.Sequence,After=facts.Sequence,Targets=r.Targets.Where(facts.Targets(f,r.Kind).Contains).ToArray()},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Discard(f);Continue(f.Id);
 }
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,37400)]private void Dying(PlayerDyingEvent fact)
 {
  if(facts.DyingCause(fact)is not{}cause||frames.Find(cause.Frame)is not ProgramSkillFrame{MouDongCheng:{Stage:MouDongChengStage.NativeUse,Dying.Count:0}r}f||!Valid(f))return;
  frames.Replace(f.Id,f=f with{MouDongCheng=r with{Dying=[fact.ResolutionId]}});events.AdvanceFact(new MouDcDyingEvent(f.Id,cause.Use,cause.Damage,fact.ResolutionId,fact.VictimSeat));native.Disable(f);
 }
 internal void UseReturned(MouDcNativeUse issued,bool attackActive)
 {
  if(frames.ActiveProgram(issued.FrameId)is not{MouDongCheng:{Stage:MouDongChengStage.NativeUse,Native:{}n,Returned:false}r}f||!Valid(f)||!SameUse(issued,n)||facts.History<MouDcUseReturnedEvent>().Any(e=>e.UseId==issued.UseId))throw new InvalidOperationException("Douwei cannot return or reward a different or already completed native Use.");
  frames.ReplaceTop(f with{MouDongCheng=r with{Stage=MouDongChengStage.ReturnedUse,Returned=true}});events.AdvanceFact(new MouDcUseReturnedEvent(f.Id,n.UseId,n.ActionId));if(!attackActive)Continue(f.Id);
 }
 internal static bool SameUse(MouDcNativeUse a,MouDcNativeUse b)=>a.FrameId==b.FrameId&&a.Source==b.Source&&a.GameplayHash==b.GameplayHash&&a.Turn==b.Turn&&a.Kind==b.Kind&&a.UseId==b.UseId&&a.ActionId==b.ActionId&&a.Targets.SequenceEqual(b.Targets);
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouDongCheng:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Mou Dong Cheng lost his exact owning request, physical payment or native return.");if(facts.Finished){Complete(f);return;}if(r.Stage is MouDongChengStage.NativeUse or MouDongChengStage.IssuingUse)return;
   if(r.Stage is MouDongChengStage.SlotChildren or MouDongChengStage.ClaimChildren or MouDongChengStage.CostChildren or MouDongChengStage.RecoveryChildren or MouDongChengStage.RestoreChildren)
   {
    if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouDongCheng!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
    if(r.Stage==MouDongChengStage.CostChildren&&facts.Alive(f.OwnerSeat))
    {if(r.Actual!=1)throw new InvalidOperationException("Douwei must discard one actual physical cost before its zero-material Use.");frames.ReplaceTop(f=f with{MouDongCheng=r with{Stage=MouDongChengStage.IssuingUse,Targets=r.Targets.Where(facts.Targets(f,r.Kind).Contains).ToArray()}});native.Use(f);return;}
    if(r.Stage==MouDongChengStage.RecoveryChildren&&facts.Alive(f.OwnerSeat)&&facts.Slots(f.OwnerSeat,true).Count>0){frames.ReplaceTop(f with{MouDongCheng=r with{Stage=MouDongChengStage.Restore}});continue;}Complete(f);return;
   }
   if(r.Stage==MouDongChengStage.ReturnedUse){if(r.Dying.Count>0&&facts.Alive(f.OwnerSeat)){frames.ReplaceTop(f=f with{MouDongCheng=r with{Stage=MouDongChengStage.RecoveryChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Recover(f);continue;}Complete(f);return;}
   if(!facts.Alive(f.OwnerSeat)||Choices(f).Count==0){Complete(f);return;}
   Prompt(f);return;
  }
 }
 private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouDcCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouDongCheng=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,37400)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouDongCheng:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,37400)]private bool Movement(ProgramSkillFrame f)=>f.MouDongCheng is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,37400)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,37400)]private bool Paid(ProgramSkillFrame f)=>f.MouDongCheng is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,37400)]private void Invariant(ProgramSkillFrame f){if(f.MouDongCheng is not null&&!Valid(f))throw new InvalidOperationException("Invalid Mou Dong Cheng owning receipt.");}
 [ProgramAiChoice(SkillProgramEffectOp.InitializeMouDongChengBaojia,SkillProgramEffectOp.ResolveMouDongChengBaojia,SkillProgramEffectOp.ClaimMouDongChengBaojia,SkillProgramEffectOp.ResolveMouDongChengDouwei,SkillProgramEffectOp.ResolveMouDongChengDelayedSettlement)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)
 {
  var r=f.MouDongCheng!;if(r.Stage==MouDongChengStage.Opening)return p.Choices.OrderBy(c=>facts.TargetScore(f.OwnerSeat,c.Targets.Single())).ThenBy(c=>c.Targets.Single()).First();
  if(r.Stage==MouDongChengStage.Cost)return p.Choices.OrderBy(c=>facts.KeepValue(f.OwnerSeat,c.Cards.Single())).First();
  if(r.Stage==MouDongChengStage.Targets)return r.Targets.Count>0?p.Choices.Single(c=>c.Parameters["branch"]=="done"):p.Choices.Where(c=>c.Parameters["branch"]=="add").OrderByDescending(c=>facts.TargetScore(f.OwnerSeat,c.Targets.Single())).FirstOrDefault()??p.Choices.Single(c=>c.Parameters["branch"]=="done");return p.Choices.First();
 }
}
