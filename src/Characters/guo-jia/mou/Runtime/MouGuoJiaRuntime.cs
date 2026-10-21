using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed class MouGuoJiaRuntime(IMouGuoJiaFacts facts,IMouGuoJiaNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts):IMouGuoJiaRuntime
{
 internal const string Xianmou="mou:xianmou",Lunshi="mou:lunshi",Yiji="mou-guo-jia:yiji",Action="mou-guo-jia-anniversary";
 internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
 internal static string GrantSource(ProgramSkillFrame f)=>$"mou-xianmou:{f.OwnerSeat}:{f.SkillInstanceId}";
 private int Sample(int owner,string instance)=>facts.History<MouGjLossSampleEvent>().SingleOrDefault(e=>e.Turn==facts.Turn&&e.Owner==owner&&e.Instance==instance)?.Count??0;
 [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,37100)]private void FreezeLosses(TurnEndedEvent ended)
 {foreach(var(seat,instance)in facts.XianmouOwners)events.AdvanceFact(new MouGjLossSampleEvent(ended.TurnNumber,seat,instance,facts.LossCount(seat)));}
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,37100,SkillProgramEffectOp.ResolveMouGuoJiaXianmou)]private bool Qualifies(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>w.Window==SkillProgramTriggerWindow.AfterTurnEnded&&Sample(c.OwnerSeat,c.SkillInstanceId)>0;
 internal bool Valid(ProgramSkillFrame f)=>f.MouGuoJia is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&r.ParentId==f.WindowContext?.ParentFrameId&&facts.Parent(f)&&r.Turn==facts.Turn&&r.X>=0&&r.Cursor>=0&&r.Cursor<=2&&Enum.IsDefined(r.Stage)&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&r.Viewed.Select(c=>c.Id).Distinct().Count()==r.Viewed.Count&&r.Selected.Distinct().Count()==r.Selected.Count&&r.Selected.All(id=>r.Viewed.Any(c=>c.Id==id))&&facts.History<MouGjStartedEvent>().Count(e=>e==new MouGjStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.ParentId,r.Yin,r.X))==1;
 public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
 {
  var f=frames.ActiveProgram(supplied.Id);if(f.MouGuoJia is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
  if(f.InstructionIndex!=1||f.WindowContext is not{}w||!facts.Parent(f)||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash)throw new InvalidOperationException("Mou Guo Jia requires his original opening, actual ended turn or damage-point parent.");
  var opening=op==SkillProgramEffectOp.InitializeMouGuoJiaXianmou;var yiji=op==SkillProgramEffectOp.ResolveMouGuoJiaYiji;var yin=!yiji&&facts.Yin(f.OwnerSeat,f.SkillInstanceId);var x=opening||yiji?0:Sample(f.OwnerSeat,f.SkillInstanceId);
  if(!opening&&!yiji&&x<=0)throw new InvalidOperationException("Xianmou lost its positive frozen actual-turn loss count.");
  var r=new MouGuoJiaReceipt(Source(f),f.GameplayHash,op,facts.Turn,w.ParentFrameId,yin,x){Stage=opening?MouGuoJiaStage.Opening:yiji?MouGuoJiaStage.YijiCard:yin?MouGuoJiaStage.Target:MouGuoJiaStage.RemoveGrantChildren,Before=facts.Sequence,After=facts.Sequence};frames.ReplaceTop(f=f with{MouGuoJia=r});events.AdvanceFact(new MouGjStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.ParentId,yin,x));
  if(!opening&&!yiji){native.State(f,!yin);if(!yin)native.YijiGrant(f,false);}if(yiji)SetView(f,native.Peek(f,2),MouGuoJiaStage.YijiCard,-1);Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
 }
 private PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int? card=null,int? target=null)=>new(new ChoiceId($"mou-guo-jia.{f.Id}.{f.MouGuoJia!.Stage}.{branch}.{card}.{target}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
 private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
 {
  var r=f.MouGuoJia!;if(r.Stage==MouGuoJiaStage.Opening)return[Choice(f,"yang","阳：观看牌堆顶五张并获得至多X张"),Choice(f,"yin","阴：观看一名角色手牌并弃置至多X张")];
  if(r.Stage==MouGuoJiaStage.Target)return facts.Living.Select(s=>Choice(f,"target","观看"+facts.Name(s)+"的手牌",target:s)).ToArray();
  if(r.Stage==MouGuoJiaStage.YijiCard)return r.Viewed.Where(facts.At).Select(c=>Choice(f,"card","选择"+c.Label,c.Id)).ToArray();
  if(r.Stage==MouGuoJiaStage.YijiTarget)return facts.Living.Select(s=>Choice(f,"recipient","交给"+facts.Name(s),target:s)).ToArray();
  if(r.Stage!=MouGuoJiaStage.Select)return[];
  var allowed=r.Viewed.Where(c=>facts.At(c)&&(!r.Yin||facts.Discardable(r.Target,c.Id))).ToArray();var choices=allowed.Where(c=>r.Selected.Contains(c.Id)||r.Selected.Count<r.X).Select(c=>Choice(f,r.Selected.Contains(c.Id)?"remove":"add",(r.Selected.Contains(c.Id)?"取消":"选择")+c.Label,c.Id)).ToList();choices.Add(Choice(f,"done",(r.Yin?"弃置":"获得")+r.Selected.Count+"张牌并完成"));return choices;
 }
 private void Prompt(ProgramSkillFrame f)
 {
  var choices=Choices(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,f.OwnerSeat,f.SkillId==Yiji?"遗计：将本次观看的两张牌分别交给一名角色。":"先谋：结算当前转换项。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=Array.AsReadOnly(choices.ToArray())},facts.Human(f.OwnerSeat));native.Publish();
 }
 [ProgramAnswer(Action)]private void Answer(PromptChoice answer)
 {
  if(frames.Top is not ProgramSkillFrame{MouGuoJia:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=f.OwnerSeat||Choices(f).SingleOrDefault(c=>c.Id==answer.Id)is not{}c||!c.Cards.SequenceEqual(answer.Cards)||!c.Targets.SequenceEqual(answer.Targets)||c.Parameters.Count!=answer.Parameters.Count||c.Parameters.Any(p=>answer.Parameters.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("Mou Guo Jia requires his exact current private viewing choice.");
  prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=c.Parameters["branch"];
  if(r.Stage==MouGuoJiaStage.Opening){native.State(f,branch=="yin");Complete(f);return;}
  if(r.Stage==MouGuoJiaStage.Target){var target=c.Targets.Single();SetView(f,facts.Hand(target),MouGuoJiaStage.Select,target);Continue(f.Id);return;}
  if(r.Stage==MouGuoJiaStage.YijiCard){frames.ReplaceTop(f with{MouGuoJia=r with{Stage=MouGuoJiaStage.YijiTarget,CardId=c.Cards.Single()}});Continue(f.Id);return;}
  if(r.Stage==MouGuoJiaStage.YijiTarget){BeginMovement(f,r with{Target=c.Targets.Single(),Selected=[r.CardId],Stage=MouGuoJiaStage.YijiChildren});return;}
  if(branch!="done"){var id=c.Cards.Single();frames.ReplaceTop(f with{MouGuoJia=r with{Selected=branch=="add"?r.Selected.Append(id).ToArray():r.Selected.Where(x=>x!=id).ToArray()}});Continue(f.Id);return;}
  BeginMovement(f,r with{Stage=MouGuoJiaStage.MovementChildren});
 }
 private void SetView(ProgramSkillFrame f,IReadOnlyList<MouGjEntity> view,MouGuoJiaStage stage,int target)
 {frames.ReplaceTop(f with{MouGuoJia=f.MouGuoJia! with{Stage=stage,Viewed=view,Selected=[],Target=target}});events.AdvanceFact(new MouGjViewedEvent(f.Id,f.OwnerSeat,target,view.Count,target<0));}
 private void BeginMovement(ProgramSkillFrame f,MouGuoJiaReceipt next)
 {frames.ReplaceTop(f=f with{MouGuoJia=next with{Before=facts.Sequence,After=facts.Sequence,Actual=0},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(next.Selected.Count>0)native.Move(f);Continue(f.Id);}
 internal void Continue(long id)
 {
  while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouGuoJia:{}r}f&&f.Id==id)
  {
   if(!Valid(f))throw new InvalidOperationException("Mou Guo Jia lost his original source, exact private view, native movement, judgment or damage receipt.");if(facts.Finished){Complete(f);return;}
   if(r.Stage==MouGuoJiaStage.Judging)return;
   if(r.Stage==MouGuoJiaStage.DamageChildren&&f.AttackAttempt is not null)return;
   if(r.Stage is MouGuoJiaStage.RemoveGrantChildren or MouGuoJiaStage.MovementChildren or MouGuoJiaStage.GrantChildren or MouGuoJiaStage.YijiChildren or MouGuoJiaStage.JudgmentChildren or MouGuoJiaStage.DamageChildren)
   {
    if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouGuoJia!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
    if(r.Stage==MouGuoJiaStage.RemoveGrantChildren){if(!facts.Alive(f.OwnerSeat)){Complete(f);return;}SetView(f,native.Peek(f,5),MouGuoJiaStage.Select,-1);continue;}
    if(r.Stage==MouGuoJiaStage.YijiChildren){var rest=r.Viewed.Where(c=>c.Id!=r.CardId).ToArray();frames.ReplaceTop(f with{MouGuoJia=r with{Stage=MouGuoJiaStage.YijiCard,Cursor=r.Cursor+1,Viewed=rest,Selected=[],CardId=0,Target=-1}});continue;}
    if(r.Stage==MouGuoJiaStage.MovementChildren)
    {
     if(!r.Yin){frames.ReplaceTop(f=f with{MouGuoJia=r with{Stage=MouGuoJiaStage.GrantChildren}});if(r.Actual<r.X&&facts.Alive(f.OwnerSeat))native.YijiGrant(f,true);continue;}
     if(r.Actual>=r.X&&facts.Alive(f.OwnerSeat)){frames.ReplaceTop(f=f with{MouGuoJia=r with{Stage=MouGuoJiaStage.Judging}});native.Judge(f);return;}Complete(f);return;
    }
    if(r.Stage==MouGuoJiaStage.JudgmentChildren&&r.LightningHit&&facts.Alive(f.OwnerSeat)){frames.ReplaceTop(f=f with{MouGuoJia=r with{Stage=MouGuoJiaStage.DamageChildren}});events.AdvanceFact(new MouGjLightningIssuedEvent(f.Id,r.JudgmentId,f.OwnerSeat,3));native.Damage(f);Continue(f.Id);return;}
    Complete(f);return;
   }
   if(!facts.Alive(f.OwnerSeat)||r.Stage==MouGuoJiaStage.YijiCard&&(r.Cursor>=2||Choices(f).Count==0)||r.Stage is MouGuoJiaStage.Target or MouGuoJiaStage.YijiTarget&&Choices(f).Count==0){Complete(f);return;}
   if(r.Stage==MouGuoJiaStage.Select){var selected=r.Selected.Where(id=>r.Viewed.Any(c=>c.Id==id&&facts.At(c)&&(!r.Yin||facts.Discardable(r.Target,id)))).Take(r.X).ToArray();if(!selected.SequenceEqual(r.Selected))frames.ReplaceTop(f=f with{MouGuoJia=r with{Selected=selected}});}
   Prompt(f);return;
  }
 }
 internal void JudgmentReturned(ProgramSkillFrame f,JudgmentResolvedEvent result,long? handoff)
 {
  if(!Valid(f)||f.MouGuoJia is not{Stage:MouGuoJiaStage.Judging}r||r.JudgmentId!=result.ResolutionId||result.ParentResolutionId!=f.Id||result.TargetSeat!=f.OwnerSeat||result.Reason!=JudgmentReasons.Lightning||facts.History<MouGjJudgmentReturnedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Xianmou cannot replace or repeat its true finalized Lightning judgment.");
  var hit=result.Suit==Suit.Spade&&result.Rank is>=2 and<=9;frames.ReplaceTop(f with{MouGuoJia=r with{Stage=MouGuoJiaStage.JudgmentChildren,JudgmentCard=result.CardId,JudgmentSuit=result.Suit,JudgmentRank=result.Rank,JudgmentHandoff=handoff,LightningHit=hit,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouGjJudgmentReturnedEvent(f.Id,result.ResolutionId,result.CardId,result.Suit,result.Rank,handoff));
 }
 private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouGjCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouGuoJia=null,PendingMovementContinuation=null});native.Finish(f);}
 [ProgramContinuation(ProgramContinuationKind.Resume,37100)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouGuoJia:not null}f||f.Id!=id)return false;Continue(id);return true;}
 [ProgramContinuation(ProgramContinuationKind.Movement,37100)]private bool Movement(ProgramSkillFrame f)=>f.MouGuoJia is not null&&Resume(f.Id);
 [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,37100)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,37100)]private bool Paid(ProgramSkillFrame f)=>f.MouGuoJia is not null&&Valid(f);
 [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,37100)]private void Invariant(ProgramSkillFrame f){if(f.MouGuoJia is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Mou Guo Jia receipt.");}
 [ProgramAiChoice(SkillProgramEffectOp.InitializeMouGuoJiaXianmou,SkillProgramEffectOp.ResolveMouGuoJiaXianmou,SkillProgramEffectOp.ResolveMouGuoJiaYiji)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="done"&&f.MouGuoJia!.Selected.Count>=Math.Min(f.MouGuoJia.X,f.MouGuoJia.Viewed.Count))??p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="add")??p.Choices.First();
}
