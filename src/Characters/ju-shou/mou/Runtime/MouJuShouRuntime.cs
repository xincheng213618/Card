using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouJuShouRuntime(IMouJuShouFacts facts,IMouJuShouNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouJuShouRuntime
{
 internal const string ZuoJun="mou:zuojun",Muwang="mou:muwang",Action="mou-ju-shou-anniversary";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal bool Valid(ProgramSkillFrame f)=>f.MouJuShou is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&facts.Parent(f)&&r.Turn==facts.Turn&&Enum.IsDefined(r.Stage)&&r.Bound.Distinct().Count()==r.Bound.Count&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&facts.History<MouJsStartedEvent>().Count(e=>e==new MouJsStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.Actor))==1;
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouJuShou is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash||!facts.Parent(f))throw new InvalidOperationException("Mou Ju Shou requires his exact issued activation or actual movement candidate.");
  if(op==SkillProgramEffectOp.ResolveMouJuShouMuwang)return StartMuwang(f);
  if(op!=SkillProgramEffectOp.ResolveMouJuShouZuoJun)throw new InvalidOperationException("This issued operation is not a Zuo Jun activation.");
  if(f.SkillId!=ZuoJun||f.SelectedTargetSeats is not[var actor]||!facts.Alive(actor))throw new InvalidOperationException("Zuo Jun requires one actual living recipient.");
  var r=new MouJuShouReceipt(Source(f),f.GameplayHash,op,facts.Turn,actor){Stage=MouJuShouStage.DrawingThree,Before=facts.Sequence,After=facts.Sequence};frames.ReplaceTop(f=f with{MouJuShou=r});events.AdvanceFact(new MouJsStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,actor));draws.BeginOperationProgramDraw(f,actor,3,new("skill-program.mou:zuojun.draw-three"));return SkillProgramStepOutcome.AwaitChild;
 }
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string text,int? card=null,int? option=null)=>new(new ChoiceId($"mou-ju-shou.{f.Id}.{f.MouJuShou!.Stage}.{branch}.{card}.{option}"),text,card is{}id?[id]:[],[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch},{"option",option?.ToString(CultureInfo.InvariantCulture)??""}});
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouJuShou!;if(r.Stage==MouJuShouStage.Choice)return[Choice(f,"keep","保留这些牌：无法使用且不计入手牌上限，直到下回合结束"),Choice(f,"use","失去1点体力，再摸一张牌，使用任意张后弃置余牌")];
  if(r.Stage==MouJuShouStage.Use)return facts.Plans(f).Select((p,i)=>Choice(f,"use",p.Action.Description,option:i)with{Cards=p.Cards,Targets=p.Targets}).Append(Choice(f,"done","结束使用并弃置剩余所得牌")).ToArray();
  if(r.Stage==MouJuShouStage.FirstLossChoice)return r.Bound.Select(id=>Choice(f,"first","选择本批首次失去的【"+facts.Label(id)+"】",id)).ToArray();
  if(r.Stage==MouJuShouStage.Penalty)return facts.Discardable(r.Actor).Select(id=>Choice(f,"discard","弃置"+facts.Label(id),id)).ToArray();return[];
 }
 private void Prompt(ProgramSkillFrame f)
 {
  var choices=Choices(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var actor=f.MouJuShou!.Actor;
  prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,actor,f.SkillId==ZuoJun?"佐军：选择保留，或失去体力后使用本次所得牌。":"暮往：再次失去收回的实体，弃置一张牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(actor));native.Publish();
 }
 [ProgramAnswer(Action)]private void Answer(PromptChoice answer)
 {
  if(frames.Top is not ProgramSkillFrame{MouJuShou:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=r.Actor||Choices(f).SingleOrDefault(c=>c.Id==answer.Id)is not{}c||!c.Cards.SequenceEqual(answer.Cards)||!c.Targets.SequenceEqual(answer.Targets)||c.Parameters.Count!=answer.Parameters.Count||c.Parameters.Any(p=>answer.Parameters.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("Mou Ju Shou requires the real recipient's exact live private choice.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=c.Parameters["branch"];
  if(r.Stage==MouJuShouStage.FirstLossChoice){ChooseFirst(f,c.Cards.Single());Continue(f.Id);return;}
  if(r.Stage==MouJuShouStage.Choice)
  {
   events.AdvanceFact(new MouJsBranchEvent(f.Id,r.Actor,branch=="use"));
   if(branch=="keep")
   {
    var grants=r.Bound.Where(id=>facts.InHand(r.Actor,id)).Select(id=>new MouJsZuoJunGrant(f.Id,r.Source,r.GameplayHash,r.Actor,id,r.Turn,facts.Sequence)).ToArray();native.SetGrants(r.Actor,facts.Grants(r.Actor).Concat(grants).ToArray());events.AdvanceFact(new MouJsGrantEvent(f.Id,r.Actor,grants.Length,r.Turn));Complete(f);return;
   }
   frames.ReplaceTop(f=f with{MouJuShou=r with{Stage=MouJuShouStage.HealthChildren,Before=facts.Sequence,After=facts.Sequence}});native.LoseHp(f);Continue(f.Id);return;
  }
  if(r.Stage==MouJuShouStage.Use&&branch=="use")
  {
   var p=facts.Plans(f)[int.Parse(c.Parameters["option"],CultureInfo.InvariantCulture)];var n=new MouJsNativeUse(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,r.Turn,r.Actor,p,0,0);frames.ReplaceTop(f=f with{MouJuShou=r with{Stage=MouJuShouStage.IssuingUse,Native=n,Before=facts.Sequence,After=facts.Sequence}});native.Use(f);return;
  }
  BeginDiscard(f,r.Stage==MouJuShouStage.Penalty?c.Cards:r.Bound.Where(id=>facts.InHand(r.Actor,id)).ToArray(),r.Stage==MouJuShouStage.Penalty);
 }
 private void BeginDiscard(ProgramSkillFrame f,IReadOnlyList<int> cards,bool penalty)
 {
  var r=f.MouJuShou!;frames.ReplaceTop(f=f with{MouJuShou=r with{Stage=penalty?MouJuShouStage.PenaltyChildren:MouJuShouStage.DiscardChildren,Discard=cards,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(cards.Count>0)native.Discard(f);Continue(f.Id);
 }
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouJuShou:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Mou Ju Shou lost his paid source, actual recipient, draw or native child receipt.");
   if(facts.Finished){Complete(f);return;}
   if(r.Stage is MouJuShouStage.DrawingThree or MouJuShouStage.DrawingOne or MouJuShouStage.IssuingUse or MouJuShouStage.Declaration or MouJuShouStage.NativeUse)return;
   if(r.Stage==MouJuShouStage.HealthChildren)
   {
    if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouJuShou!;
    if(!facts.History<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==id&&e.TargetSeat==r.Actor&&e.Amount==1)){native.LoseHp(f);if(frames.Top?.Id!=id||native.Drain(f))return;}
    if(!facts.Alive(r.Actor)){Complete(frames.ActiveProgram(id));return;}
    frames.ReplaceTop(f=f with{MouJuShou=r with{Stage=MouJuShouStage.DrawingOne}});draws.BeginOperationProgramDraw(f,r.Actor,1,new("skill-program.mou:zuojun.draw-one"));return;
   }
   if(r.Stage is MouJuShouStage.DrawThreeChildren or MouJuShouStage.DrawOneChildren or MouJuShouStage.DiscardChildren or MouJuShouStage.PenaltyChildren or MouJuShouStage.RecoverChildren or MouJuShouStage.ReturnedUse)
   {
    if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouJuShou!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
    if(r.Stage is MouJuShouStage.DiscardChildren or MouJuShouStage.PenaltyChildren){Complete(f);return;}
    if(r.Stage==MouJuShouStage.RecoverChildren){if(r.Penalty&&facts.Alive(r.Actor)){frames.ReplaceTop(f with{MouJuShou=r with{Stage=MouJuShouStage.Penalty}});continue;}Complete(f);return;}
    frames.ReplaceTop(f with{MouJuShou=r with{Stage=r.Stage==MouJuShouStage.DrawThreeChildren?MouJuShouStage.Choice:MouJuShouStage.Use,Draw=null,Native=null,DeclarationId=0,DeclarationPassed=false}});continue;
   }
   if(!facts.Alive(r.Actor)){Complete(f);return;}
   if(r.Stage==MouJuShouStage.Penalty&&Choices(f).Count==0){Complete(f);return;}
   Prompt(f);return;
  }
 }
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouJuShouZuoJun)]private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
 {
  if(!Valid(f)||f.MouJuShou is not{Stage:MouJuShouStage.DrawingThree or MouJuShouStage.DrawingOne,Draw:null}r||outcome.RecipientSeat!=r.Actor||outcome.RequestedCount!=(r.Stage==MouJuShouStage.DrawingThree?3:1))throw new InvalidOperationException("Zuo Jun requires his actual native three-card or one-card draw return.");
  frames.Replace(f.Id,f with{MouJuShou=r with{Stage=r.Stage==MouJuShouStage.DrawingThree?MouJuShouStage.DrawThreeChildren:MouJuShouStage.DrawOneChildren,Bound=r.Bound.Concat(outcome.ActualDrawIds).Distinct().ToArray(),Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouJsDrawReturnedEvent(f.Id,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);
 }
 internal void Returned(MouJsNativeUse n,bool attack)
 {
  var f=frames.ActiveProgram(n.FrameId);if(!Valid(f)||f.MouJuShou is not{Stage:MouJuShouStage.NativeUse,Native:{}r}p||!SameUse(r,n)||facts.History<MouJsUseReturnedEvent>().Any(e=>e.UseId==n.UseId))throw new InvalidOperationException("Zuo Jun cannot repeat or replace the returned actual Use.");
  frames.ReplaceTop(f with{MouJuShou=p with{Stage=MouJuShouStage.ReturnedUse,Bound=p.Bound.Except(n.Plan.Cards).ToArray(),After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouJsUseReturnedEvent(f.Id,n.UseId,n.ActionId));if(!attack)Continue(f.Id);
 }
 private void Complete(ProgramSkillFrame f){if(f.MouJuShou is{Operation:SkillProgramEffectOp.ResolveMouJuShouMuwang,MuwangOrdinal:>=0}r){if(facts.History<MouJsMuwangSettledEvent>().Any(e=>e.Ordinal==r.MuwangOrdinal))throw new InvalidOperationException("Muwang cannot settle one actual movement opportunity twice.");events.AdvanceFact(new MouJsMuwangSettledEvent(f.Id,r.MuwangOrdinal));}events.AdvanceFact(new MouJsCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouJuShou=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,36900)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouJuShou:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,36900)]private bool Movement(ProgramSkillFrame f)=>f.MouJuShou is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36900)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36900)]private bool Paid(ProgramSkillFrame f)=>f.MouJuShou is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36900)]private void Invariant(ProgramSkillFrame f){if(f.MouJuShou is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Mou Ju Shou receipt.");}
 [ProgramAiChoice(SkillProgramEffectOp.ResolveMouJuShouZuoJun,SkillProgramEffectOp.ResolveMouJuShouMuwang)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="keep")??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="done")??p.Choices.First();
 internal static bool SamePlan(MouJsPlan a,MouJsPlan b)=>a.Form==b.Form&&a.Kind==b.Kind&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Action.Kind==b.Action.Kind&&a.Action.CardId==b.Action.CardId&&a.Action.PlayedCardKind==b.Action.PlayedCardKind&&a.Action.TargetCardId==b.Action.TargetCardId&&a.Action.ConversionSource==b.Action.ConversionSource&&(a.Action.AdditionalConversionSources??[]).SequenceEqual(b.Action.AdditionalConversionSources??[])&&a.Action.TargetSeats.SequenceEqual(b.Action.TargetSeats);
 internal static bool SameUse(MouJsNativeUse a,MouJsNativeUse b)=>a.FrameId==b.FrameId&&a.Instruction==b.Instruction&&a.Source==b.Source&&a.GameplayHash==b.GameplayHash&&a.Turn==b.Turn&&a.Actor==b.Actor&&a.UseId==b.UseId&&a.ActionId==b.ActionId&&a.Targets.SequenceEqual(b.Targets)&&SamePlan(a.Plan,b.Plan);
}
