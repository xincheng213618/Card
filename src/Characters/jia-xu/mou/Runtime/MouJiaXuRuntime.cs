using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed class MouJiaXuRuntime(IMouJiaXuFacts facts,IMouJiaXuNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouJiaXuRuntime
{
 internal const string Sushen="mou:sushen",Fumou="mou:fumou",Rushi="mou:rushi",Action="mou-jia-xu-anniversary",TransferReason="skill-program.mou:fumou.obtain",DiscardReason="skill-program.mou:rushi.discard";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal MouJiaXuRecordedEvent? Recorded(int owner)=>facts.History<MouJiaXuRecordedEvent>().LastOrDefault(e=>e.Source.OwnerSeat==owner&&e.Source.SkillId==Sushen&&e.GameplayHash==facts.Program(Sushen)?.GameplayHash&&facts.History<MouJiaXuDerivedGrantedEvent>().Any(g=>g.FrameId==e.FrameId&&g.Owner==owner));
 [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts,36500,SkillProgramEffectOp.ResolveMouJiaXuFumou,SkillProgramEffectOp.RecordMouJiaXuSushen,SkillProgramEffectOp.RestoreMouJiaXuRushi)]private bool CanActivate(ProgramActivationLegalityContext c)=>facts.ActualPlay(c.OwnerSeat)&&(c.SkillId==Rushi?Recorded(c.OwnerSeat)is not null:c.SkillId!=Fumou||facts.Living().Count>=(facts.Yin(c.OwnerSeat,facts.FumouInstance(c.OwnerSeat))?2:3));
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouJiaXu is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash||!facts.Parent(f))throw new InvalidOperationException("Mou Jia Xu requires his exact accepted own-Play or GameStarting parent.");
  var instance=facts.FumouInstance(f.OwnerSeat);var yin=facts.Yin(f.OwnerSeat,instance);var stage=op switch{SkillProgramEffectOp.InitializeMouJiaXuFumou=>MouJiaXuStage.Initial,SkillProgramEffectOp.RecordMouJiaXuSushen=>MouJiaXuStage.GrantChildren,SkillProgramEffectOp.RestoreMouJiaXuRushi=>MouJiaXuStage.HealthChildren,_=>MouJiaXuStage.Target};
  var r=new MouJiaXuReceipt(Source(f),f.GameplayHash,op,facts.Turn,yin,instance){Stage=stage,Before=facts.Sequence,After=facts.Sequence};
  if(op==SkillProgramEffectOp.RestoreMouJiaXuRushi)r=r with{Record=Recorded(f.OwnerSeat)??throw new InvalidOperationException("Rushi has no genuinely recorded and granted Sushen origin."),HpBefore=facts.Hp(f.OwnerSeat),HpAfter=Math.Min(facts.MaximumHp(f.OwnerSeat),Recorded(f.OwnerSeat)!.Hp)};
  frames.ReplaceTop(f=f with{MouJiaXu=r});events.AdvanceFact(new MouJiaXuStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,yin,instance));
  if(op==SkillProgramEffectOp.RecordMouJiaXuSushen){var record=new MouJiaXuRecordedEvent(f.Id,r.Source,r.GameplayHash,facts.Hp(f.OwnerSeat),facts.Hand(f.OwnerSeat).Count,yin);frames.ReplaceTop(f=f with{MouJiaXu=r with{Record=record},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(record);native.Grant(f);}
  else if(op==SkillProgramEffectOp.RestoreMouJiaXuRushi){frames.ReplaceTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.SetHealth(f);}
  else if(op==SkillProgramEffectOp.ResolveMouJiaXuFumou)native.State(f,!yin);
  Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 internal bool Valid(ProgramSkillFrame f)=>f.MouJiaXu is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.Operation==(f.TriggerId is not null?SkillProgramEffectOp.InitializeMouJiaXuFumou:f.SkillId==Sushen?SkillProgramEffectOp.RecordMouJiaXuSushen:f.SkillId==Rushi?SkillProgramEffectOp.RestoreMouJiaXuRushi:SkillProgramEffectOp.ResolveMouJiaXuFumou)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&facts.Parent(f)&&r.Turn==facts.Turn&&Enum.IsDefined(r.Stage)&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&r.Requested>=0&&r.DrawCursor is>=0 and<=2&&r.Selected.Distinct().Count()==r.Selected.Count&&r.Frozen.Distinct().Count()==r.Frozen.Count&&r.Remaining.Distinct().Count()==r.Remaining.Count&&r.Remaining.All(r.Frozen.Contains)&&r.View.Select(c=>c.Id).Distinct().Count()==r.View.Count&&r.View.All(c=>c.Holder==r.Holder)&&facts.History<MouJiaXuStartedEvent>().Count(e=>e==new MouJiaXuStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.Yin,r.FumouInstance))==1&&(r.Record is null||r.Record.Source.OwnerSeat==f.OwnerSeat&&r.Record.Source.SkillId==Sushen&&r.Record.GameplayHash==facts.Program(Sushen)?.GameplayHash&&facts.History<MouJiaXuRecordedEvent>().Count(e=>e==r.Record)==1);
 private int Chooser(ProgramSkillFrame f)=>f.MouJiaXu is{Stage:MouJiaXuStage.Use,Holder:>=0}r?r.Holder:f.OwnerSeat;
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int? card=null,int? target=null)=>new(new ChoiceId($"mou-jia-xu-anniversary.{f.Id}.{f.MouJiaXu!.Stage}.{branch}.{card}.{target}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
 private int DiscardNeed(ProgramSkillFrame f)=>Math.Min(Math.Max(0,facts.Hand(f.OwnerSeat).Count-f.MouJiaXu!.Record!.HandCount),facts.Hand(f.OwnerSeat,true).Count);
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouJiaXu!;
  if(r.Stage==MouJiaXuStage.Initial)return[Choice(f,"yang","阳：转移展示牌并分别摸牌"),Choice(f,"yin","阴：持有者依次真正使用展示牌")];
  if(r.Stage==MouJiaXuStage.Target)return facts.Living().Where(s=>s!=f.OwnerSeat).Select(s=>Choice(f,"target",facts.Name(s),target:s)).ToArray();
  if(r.Stage==MouJiaXuStage.Show){var current=facts.Hand(r.Holder).Select(c=>c.Id).ToHashSet();return r.View.Where(c=>current.Contains(c.Id)&&(r.Selected.Contains(c.Id)||r.Selected.Count<(r.View.Count+1)/2)).Select(c=>Choice(f,r.Selected.Contains(c.Id)?"remove":"add",(r.Selected.Contains(c.Id)?"取消展示":"展示")+c.Label,c.Id)).Append(Choice(f,"done","确认展示"+r.Selected.Count+"张牌")).ToArray();}
  if(r.Stage==MouJiaXuStage.Recipient)return facts.Living().Where(s=>s!=f.OwnerSeat&&s!=r.Holder).Select(s=>Choice(f,"recipient",facts.Name(s),target:s)).ToArray();
  if(r.Stage==MouJiaXuStage.Use)return facts.UseOptions(f).Select((a,i)=>new PromptChoice(new ChoiceId($"mou-jia-xu-anniversary.{f.Id}.use.{i}"),a.Description,[a.CardId!.Value],a.TargetSeats,new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch","use"},{"option",i.ToString(CultureInfo.InvariantCulture)}})).ToArray();
  if(r.Stage!=MouJiaXuStage.Discard)return[];var need=DiscardNeed(f);var choices=facts.Hand(f.OwnerSeat,true).Where(c=>r.Selected.Contains(c.Id)||r.Selected.Count<need).Select(c=>Choice(f,r.Selected.Contains(c.Id)?"remove":"add",(r.Selected.Contains(c.Id)?"取消":"选择")+c.Label,c.Id)).ToList();if(r.Selected.Count==need)choices.Add(Choice(f,"done","确认弃置"+need+"张手牌"));return choices;
 }
 private void Publish(ProgramSkillFrame f)
 {
  var choices=Choices(f);var chooser=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,chooser,f.MouJiaXu!.Stage==MouJiaXuStage.Show?"覆谋：私下观看其全部手牌，选择至多一半向上取整张公开展示。":"谋贾诩：选择本次技能的下一项。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(chooser));native.Publish();
 }
 [ProgramAnswer(Action)]private void Answer(PromptChoice chosen)
 {
  if(frames.Top is not ProgramSkillFrame{MouJiaXu:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||Choices(f).SingleOrDefault(c=>c.Id==chosen.Id)is not{}c||!c.Cards.SequenceEqual(chosen.Cards)||!c.Targets.SequenceEqual(chosen.Targets)||c.Parameters.Count!=chosen.Parameters.Count||c.Parameters.Any(p=>chosen.Parameters.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("Mou Jia Xu requires the actual private chooser's exact published choice.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=chosen.Parameters["branch"];
  if(r.Stage==MouJiaXuStage.Initial){native.State(f,branch=="yin");Complete(f);return;}
  if(r.Stage==MouJiaXuStage.Target){var holder=chosen.Targets.Single();var hand=facts.Hand(holder);frames.ReplaceTop(f with{MouJiaXu=r with{Holder=holder,View=hand,Stage=MouJiaXuStage.Show}});events.AdvanceFact(new MouJiaXuViewedEvent(f.Id,f.OwnerSeat,holder,hand.Count));Continue(f.Id);return;}
  if(r.Stage is MouJiaXuStage.Show or MouJiaXuStage.Discard)
  {
   if(branch!="done"){var id=chosen.Cards.Single();frames.ReplaceTop(f with{MouJiaXu=r with{Selected=branch=="add"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});Continue(f.Id);return;}
   if(r.Stage==MouJiaXuStage.Show){frames.ReplaceTop(f=f with{MouJiaXu=r with{Frozen=r.Selected,Remaining=r.Selected,Requested=r.Selected.Count,Stage=r.Yin?MouJiaXuStage.Use:MouJiaXuStage.Recipient}});native.Reveal(f);Continue(f.Id);return;}
   StartDiscard(f);return;
  }
  if(r.Stage==MouJiaXuStage.Recipient){frames.ReplaceTop(f=f with{MouJiaXu=r with{Recipient=chosen.Targets.Single(),Stage=MouJiaXuStage.TransferChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Transfer(f);Continue(f.Id);return;}
  var action=facts.UseOptions(f)[int.Parse(chosen.Parameters["option"],CultureInfo.InvariantCulture)];var n=new MouJiaXuNativeUse(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,r.Turn,r.Holder,action.CardId!.Value,facts.UseKind(r.Holder,action),action,0,0){Targets=action.TargetSeats};frames.ReplaceTop(f=f with{MouJiaXu=r with{Stage=MouJiaXuStage.IssuingUse,Native=n}});native.Use(f);
 }
 private void StartDiscard(ProgramSkillFrame f){var r=f.MouJiaXu!;frames.ReplaceTop(f=f with{MouJiaXu=r with{Stage=MouJiaXuStage.DiscardChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(r.Selected.Count>0)native.Discard(f);Continue(f.Id);}
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouJiaXu:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Mou Jia Xu lost his exact paid frame, record or child continuation.");if(facts.Finished){Complete(f);return;}
   if(r.Stage is MouJiaXuStage.Initial or MouJiaXuStage.Target or MouJiaXuStage.Show or MouJiaXuStage.Recipient or MouJiaXuStage.Use){if((r.Stage is MouJiaXuStage.Show or MouJiaXuStage.Use)&&!facts.Alive(r.Holder)||Choices(f).Count==0||!facts.Alive(Chooser(f))){Complete(f);return;}Publish(f);return;}
   if(r.Stage==MouJiaXuStage.AdjustHand){if(!facts.Alive(f.OwnerSeat)){Complete(f);return;}var difference=r.Record!.HandCount-facts.Hand(f.OwnerSeat).Count;if(difference>0){frames.ReplaceTop(f=f with{MouJiaXu=r with{Stage=MouJiaXuStage.Drawing,Requested=difference}});Draw(f,f.OwnerSeat,difference);return;}frames.ReplaceTop(f with{MouJiaXu=r with{Stage=difference<0?MouJiaXuStage.Discard:MouJiaXuStage.RestoreState,Selected=[]}});continue;}
   if(r.Stage==MouJiaXuStage.Discard){var allowed=facts.Hand(f.OwnerSeat,true).Select(c=>c.Id).ToHashSet();var selected=r.Selected.Where(allowed.Contains).Take(DiscardNeed(f)).ToArray();if(!selected.SequenceEqual(r.Selected))frames.ReplaceTop(f=f with{MouJiaXu=r=r with{Selected=selected}});if(DiscardNeed(f)==0){StartDiscard(f);return;}Publish(f);return;}
   if(r.Stage==MouJiaXuStage.RestoreState){frames.ReplaceTop(f=f with{MouJiaXu=r with{Stage=MouJiaXuStage.StateChildren},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.State(f,r.Record!.Yin);continue;}
   if(r.Stage is MouJiaXuStage.Drawing or MouJiaXuStage.IssuingUse or MouJiaXuStage.NativeUse)return;
   if(r.Stage==MouJiaXuStage.ReturnedUse){frames.ReplaceTop(f with{MouJiaXu=r with{Remaining=r.Remaining.Where(c=>c!=r.Native!.CardId).ToArray(),Native=null,Stage=MouJiaXuStage.Use}});continue;}
   if(native.Drain(f))return;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
   if(r.Stage==MouJiaXuStage.HealthChildren){frames.ReplaceTop(f with{MouJiaXu=r with{Stage=MouJiaXuStage.AdjustHand}});continue;}
   if(r.Stage==MouJiaXuStage.DiscardChildren){frames.ReplaceTop(f with{MouJiaXu=r with{Stage=MouJiaXuStage.RestoreState}});continue;}
   if(r.Stage==MouJiaXuStage.DrawChildren)
   {if(r.Operation==SkillProgramEffectOp.RestoreMouJiaXuRushi){frames.ReplaceTop(f with{MouJiaXu=r with{Stage=MouJiaXuStage.RestoreState,Draw=null}});continue;}frames.ReplaceTop(f=f with{MouJiaXu=r=r with{DrawCursor=r.DrawCursor+1,Draw=null,Stage=MouJiaXuStage.TransferChildren}});}
   if(r.Stage==MouJiaXuStage.TransferChildren){if(r.DrawCursor>=2||r.Requested==0){Complete(f);return;}var recipient=r.DrawCursor==0?f.OwnerSeat:r.Holder;if(!facts.Alive(recipient)){frames.ReplaceTop(f with{MouJiaXu=r with{DrawCursor=r.DrawCursor+1}});continue;}frames.ReplaceTop(f=f with{MouJiaXu=r with{Stage=MouJiaXuStage.Drawing}});Draw(f,recipient,r.Requested);return;}
   Complete(f);return;
  }
 }
 private void Draw(ProgramSkillFrame f,int recipient,int requested)=>draws.BeginOperationProgramDraw(f,recipient,requested,new("skill-program.mou:jia-xu.draw"));
 [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouJiaXuFumou,SkillProgramEffectOp.RestoreMouJiaXuRushi)]private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
 {if(!Valid(f)||f.MouJiaXu is not{Stage:MouJiaXuStage.Drawing,Draw:null}r||outcome.RequestedCount!=r.Requested||outcome.RecipientSeat!=(r.Operation==SkillProgramEffectOp.RestoreMouJiaXuRushi||r.DrawCursor==0?f.OwnerSeat:r.Holder))throw new InvalidOperationException("Mou Jia Xu requires the exact next native Draw and actual replacement receipt.");frames.Replace(f.Id,f with{MouJiaXu=r with{Stage=MouJiaXuStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouJiaXuDrawReturnedEvent(f.Id,r.DrawCursor,outcome.RecipientSeat,r.Requested,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);}
 internal void Returned(MouJiaXuNativeUse n,bool attack){if(frames.Find(n.FrameId)is not ProgramSkillFrame{MouJiaXu:{Native:{}paid}r}f||!Valid(f)||!SameUse(n,paid))throw new InvalidOperationException("Fumou requires its exact once-native displayed entity return.");frames.Replace(f.Id,f with{MouJiaXu=r with{Stage=MouJiaXuStage.ReturnedUse}});events.AdvanceFact(new MouJiaXuUseReturnedEvent(f.Id,n.UseId,n.ActionId));if(!attack)frames.AdvanceProgram(f.Id);}
 internal static bool SameUse(MouJiaXuNativeUse a,MouJiaXuNativeUse b)=>(a.FrameId,a.Instruction,a.Source,a.GameplayHash,a.Turn,a.Actor,a.CardId,a.Kind,a.UseId,a.ActionId)==(b.FrameId,b.Instruction,b.Source,b.GameplayHash,b.Turn,b.Actor,b.CardId,b.Kind,b.UseId,b.ActionId)&&a.Targets.SequenceEqual(b.Targets)&&a.Action.Kind==b.Action.Kind&&a.Action.CardId==b.Action.CardId&&a.Action.PlayedCardKind==b.Action.PlayedCardKind&&a.Action.TargetCardId==b.Action.TargetCardId&&a.Action.ConversionSource==b.Action.ConversionSource&&a.Action.TargetSeats.SequenceEqual(b.Action.TargetSeats)&&(a.Action.AdditionalConversionSources??[]).SequenceEqual(b.Action.AdditionalConversionSources??[]);
 private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouJiaXuCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouJiaXu=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,36500)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouJiaXu:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,36500)]private bool Movement(ProgramSkillFrame f)=>f.MouJiaXu is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36500)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36500)]private bool Paid(ProgramSkillFrame f)=>f.MouJiaXu is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36500)]private void Invariant(ProgramSkillFrame f){if(f.MouJiaXu is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Mou Jia Xu continuation.");}
 [ProgramAiChoice(SkillProgramEffectOp.InitializeMouJiaXuFumou,SkillProgramEffectOp.ResolveMouJiaXuFumou,SkillProgramEffectOp.RecordMouJiaXuSushen,SkillProgramEffectOp.RestoreMouJiaXuRushi)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="done"&&f.MouJiaXu!.Selected.Count>0)??p.Choices.First();
}
