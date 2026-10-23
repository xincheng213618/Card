using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouSimaYiRuntime(IMouSimaYiFacts facts,IMouSimaYiNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouSimaYiRuntime
{
    internal const string Action="mou-sima-yi-anniversary",GiveReason="skill-program.mou:quanmou.give",DrawReason="skill-program.mou:pingliao.draw";
    internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts,36200,SkillProgramEffectOp.ResolveMouSimaYiQuanmou)]
    private bool CanActivate(CharacterState owner)=>facts.ActualPlay(owner.Seat)&&Eligible(owner.Seat).Count>0;
    private IReadOnlyList<int> Eligible(int owner)=>facts.InRange(owner).Where(t=>!facts.History<MouSmDebtIssuedEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Target==t&&e.Turn==facts.Turn&&e.TurnOwner==facts.TurnOwner&&e.PhaseId==facts.PhaseId)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,36200,SkillProgramEffectOp.ResolveMouSimaYiPingliao,SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage,SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage)]
    private bool Offer(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>t.Effects.Single().Op switch
    {
        SkillProgramEffectOp.ResolveMouSimaYiPingliao=>facts.PrivateUse(c.OwnerSeat,w)is not null,
        SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage=>facts.Debt(c.OwnerSeat,w,false)is not null,
        _=>facts.Debt(c.OwnerSeat,w,true)is not null
    };
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);if(f.MouSimaYi is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.InstructionIndex!=1||facts.Skill(f.SkillId).Program?.GameplayHash!=f.GameplayHash||!facts.Parent(f))throw new InvalidOperationException("Mou Sima Yi lost his original registered native parent.");
        var use=op==SkillProgramEffectOp.ResolveMouSimaYiPingliao?facts.PrivateUse(f.OwnerSeat,f.WindowContext):null;
        var debt=op is SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage or SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage?facts.Debt(f.OwnerSeat,f.WindowContext,op==SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage):null;
        if(op==SkillProgramEffectOp.ResolveMouSimaYiPingliao&&use is null||op is SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage or SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage&&debt is null||op==SkillProgramEffectOp.ResolveMouSimaYiQuanmou&&!facts.ActualPlay(f.OwnerSeat))throw new InvalidOperationException("Mou Sima Yi cannot borrow another native damage, Slash or Play.");
        var r=new MouSmReceipt(Source(f),f.GameplayHash,op,facts.Turn,facts.TurnOwner,facts.PhaseId,f.WindowContext?.ParentFrameId??0)
        { Stage=op==SkillProgramEffectOp.InitializeMouSimaYiQuanmou?MouSmStage.Polarity:op==SkillProgramEffectOp.ResolveMouSimaYiQuanmou?MouSmStage.Target:op==SkillProgramEffectOp.ResolveMouSimaYiPingliao?MouSmStage.Respondent:MouSmStage.SpreadTargets,
            Target=debt?.Target??-1,Yin=facts.Yin(f.OwnerSeat,f.SkillInstanceId),DebtId=debt?.FrameId??0,UseId=use?.UseId??0,ActionId=use?.ActionId??0,Targets=use?.Targets??[],Respondents=use is null?[]:facts.InRange(f.OwnerSeat),Before=facts.Sequence,After=facts.Sequence };
        frames.ReplaceTop(f=f with{MouSimaYi=r});events.AdvanceFact(new MouSmStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.TurnOwner,r.PhaseId,r.ParentId,r.UseId,r.ActionId));
        if(debt is not null)events.AdvanceFact(new MouSmDebtConsumedEvent(debt.FrameId,f.Id,r.ParentId));
        if(op==SkillProgramEffectOp.PreventMouSimaYiQuanmouDamage){native.Prevent(f);Complete(f);return SkillProgramStepOutcome.AwaitChild;}
        Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)=>f.MouSimaYi is{}r&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Skill(f.SkillId).Program?.GameplayHash==r.GameplayHash&&facts.Parent(f)&&r.Turn==facts.Turn&&r.TurnOwner==facts.TurnOwner&&r.PhaseId==facts.PhaseId&&r.Cursor>=0&&r.Before>=0&&r.After>=r.Before&&r.After<=facts.Sequence&&r.Targets.Distinct().Count()==r.Targets.Count&&r.PlayedSeats.Distinct().Count()==r.PlayedSeats.Count&&r.Respondents.Distinct().Count()==r.Respondents.Count&&facts.History<MouSmStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Operation==r.Operation&&e.ParentId==r.ParentId&&e.UseId==r.UseId&&e.ActionId==r.ActionId)==1;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;PromptChoice C(string key,string label,int? target=null,IReadOnlyList<int>? cards=null)=>new(new($"mou-sm.{f.Id}.{r.Stage}.{r.Cursor}.{r.Targets.Count}.{key}.{target}"),label,cards??[],target is{}t?[t]:[],new Dictionary<string,string>{{"program-action",Action},{"branch",key},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
        if(r.Stage==MouSmStage.Polarity)return[C("yang","阳：防止对方下一次伤害"),C("yin","阴：伤害后可伤害其他角色")];
        if(r.Stage==MouSmStage.Target)return Eligible(f.OwnerSeat).Select(t=>C("target",$"令{facts.Name(t)}交给你一张牌",t)).ToArray();
        if(r.Stage==MouSmStage.Give)
        {
            var gifts=facts.Gifts(f,r.Target);
            // A previously accepted human Give could only have had one option. Keep its recorded ID replayable.
            return gifts.Select(c=>
            {
                var choice=C("give",$"权谋 · 将【{c.Label}】交给{facts.Name(f.OwnerSeat)}",cards:[c.Id]);
                return gifts.Count==1?choice:choice with{Id=new ChoiceId($"mou-sm.{f.Id}.{r.Stage}.{r.Cursor}.{r.Targets.Count}.give.{c.Id}")};
            }).ToArray();
        }
        if(r.Stage==MouSmStage.Respondent)return facts.Plays(f).Select((p,i)=>PlayedChoice(p,i)).Append(C("decline","不打出红色基本牌")).ToArray();
        PromptChoice PlayedChoice(MouSmPlayedPlan p,int i)
        {
            var c=C("play-"+i,(p.Source is{}src?$"【{facts.Skill(src.SkillId).Name}】":"")+$"打出红色【{CardCatalog.Get(p.Kind).DisplayName}】",cards:p.Cards);
            var parameters=new Dictionary<string,string>(c.Parameters){["response-card-kind"]=p.Kind.ToString()};if(p.Source is{}source){parameters["conversion-skill"]=source.SkillId;parameters["conversion-binding"]=source.BindingId;parameters["conversion-instance"]=source.SkillInstanceId;}return c with{Parameters=parameters};
        }
        if(r.Stage==MouSmStage.SpreadTargets)return facts.Living().Where(t=>t!=f.OwnerSeat&&t!=r.Target&&!r.Targets.Contains(t)&&r.Targets.Count<3).Select(t=>C("spread",$"对{facts.Name(t)}造成1点伤害",t)).Append(C("finish","确认选择")).ToArray();
        return[];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;var actor=r.Stage==MouSmStage.Give?r.Target:r.Stage==MouSmStage.Respondent?r.Actor:f.OwnerSeat;var choices=Choices(f);
        if(choices.Count==0){if(r.Stage==MouSmStage.Give){IssueDebt(f);return;}Complete(f);return;}
        frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=facts.Skill(f.SkillId);
        prompts.Set(new(DecisionKind.ProgramTrigger,actor,r.Stage==MouSmStage.Respondent?"平辽：杀的目标尚未公开，你可以打出一张红色基本牌。":"权谋：选择当前合法选项。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,TargetSeat=actor,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)},facts.Human(actor));native.Publish();
    }
    [ProgramAnswer(Action)]private void Answer(PromptChoice choice)
    {
        if(frames.Top is not ProgramSkillFrame{MouSimaYi:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=(r.Stage==MouSmStage.Give?r.Target:r.Stage==MouSmStage.Respondent?r.Actor:f.OwnerSeat)||!Choices(f).Any(c=>Same(c,choice)))throw new InvalidOperationException("Mou Sima Yi requires his exact current private participant choice.");
        prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var key=choice.Parameters["branch"];
        if(r.Stage==MouSmStage.Polarity){events.AdvanceFact(new MouSmPolarityEvent(f.Id,r.Source,key=="yin"));Complete(f);return;}
        if(r.Stage==MouSmStage.Target){frames.ReplaceTop(f=f with{MouSimaYi=r with{Target=choice.Targets.Single(),Stage=MouSmStage.Give}});Continue(f.Id);return;}
        if(r.Stage==MouSmStage.Give)
        {
            var entity=facts.Gifts(f,r.Target).Single(c=>c.Id==choice.Cards.Single());frames.ReplaceTop(f=f with{MouSimaYi=r with{Stage=MouSmStage.GiveChildren,Materials=[entity],Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Give(f);Continue(f.Id);return;
        }
        if(r.Stage==MouSmStage.Respondent)
        {
            if(key=="decline"){events.AdvanceFact(new MouSmReplyEvent(f.Id,r.Cursor,r.Actor,false));frames.ReplaceTop(f with{MouSimaYi=r with{Cursor=r.Cursor+1}});Continue(f.Id);return;}
            var plan=facts.Plays(f)[int.Parse(key[5..],CultureInfo.InvariantCulture)];var declaration=facts.Declaration(plan);frames.ReplaceTop(f=f with{MouSimaYi=r with{Stage=declaration?MouSmStage.PlayedDeclaration:MouSmStage.PlayedAccepted,Played=plan,Response=null,ResponseActionId=0,Before=facts.Sequence,After=facts.Sequence}});if(declaration)native.Declare(f);else native.Play(f);return;
        }
        if(key=="spread"){frames.ReplaceTop(f=f with{MouSimaYi=r with{Targets=r.Targets.Append(choice.Targets.Single()).ToArray()}});Publish(f);return;}
        frames.ReplaceTop(f with{MouSimaYi=r with{Stage=MouSmStage.DamageChildren,Cursor=0}});Continue(f.Id);
    }
    private static bool Same(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(p=>b.Parameters.GetValueOrDefault(p.Key)==p.Value);
    private void IssueDebt(ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;events.AdvanceFact(new MouSmDebtIssuedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner,r.PhaseId,r.Target,r.Yin));events.AdvanceFact(new MouSmPolarityEvent(f.Id,r.Source,!r.Yin));Complete(f);
    }
    internal void Continue(long id)
    {
        while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouSimaYi:{}r}f&&f.Id==id)
        {
            if(!Valid(f))throw new InvalidOperationException("Mou Sima Yi lost his owning paid continuation.");
            if(r.Stage is MouSmStage.Polarity or MouSmStage.Target or MouSmStage.SpreadTargets){if(!facts.Alive(f.OwnerSeat)){Complete(f);return;}Publish(f);return;}
            if(r.Stage==MouSmStage.Give){if(!facts.Alive(r.Target)||facts.Gifts(f,r.Target).Count==0){IssueDebt(f);return;}Publish(f);return;}
            if(r.Stage==MouSmStage.Respondent)
            {
                if(r.Cursor>=r.Respondents.Count||facts.Finished){Disclose(f);return;}var actor=r.Respondents[r.Cursor];if(!facts.Alive(actor)){frames.ReplaceTop(f with{MouSimaYi=r with{Cursor=r.Cursor+1}});continue;}
                frames.ReplaceTop(f=f with{MouSimaYi=r with{Actor=actor}});Publish(f);return;
            }
            if(r.Stage is MouSmStage.PlayedAccepted or MouSmStage.PlayedCompleted or MouSmStage.PlayedDeclaration or MouSmStage.Drawing)return;
            if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouSimaYi!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
            if(r.Stage==MouSmStage.GiveChildren){IssueDebt(f);return;}
            if(r.Stage==MouSmStage.PlayedCostChildren){native.CompletePlayed(f);return;}
            if(r.Stage==MouSmStage.PlayedChildren){frames.ReplaceTop(f with{MouSimaYi=r with{Stage=MouSmStage.Respondent,Played=null,Response=null,ResponseActionId=0}});continue;}
            if(r.Stage==MouSmStage.DrawChildren){Complete(f);return;}
            if(r.Stage==MouSmStage.DamageChildren)
            {
                if(r.Cursor>=r.Targets.Count||facts.Finished||!facts.Alive(f.OwnerSeat)){Complete(f);return;}var target=r.Targets[r.Cursor];frames.ReplaceTop(f=f with{MouSimaYi=r with{Cursor=r.Cursor+1,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                if(facts.Alive(target)){events.AdvanceFact(new MouSmDamageIssuedEvent(f.Id,r.Cursor,target));native.Damage(f,target);}continue;
            }
            return;
        }
    }
    private void Disclose(ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;native.Disclose(f);foreach(var t in r.Targets.Where(t=>!r.PlayedSeats.Contains(t)&&facts.Alive(t)))native.ProhibitHand(f,t);
        if(!r.PlayedSeats.Any(t=>!r.Targets.Contains(t))||!facts.Alive(f.OwnerSeat)||facts.Finished){Complete(f);return;}
        events.AdvanceFact(new MouSmSlashAllowanceEvent(f.Id,r.Source,r.Turn,r.TurnOwner,r.PhaseId));frames.ReplaceTop(f=f with{MouSimaYi=r with{Stage=MouSmStage.Drawing}});draws.BeginOperationProgramDraw(f,f.OwnerSeat,2,new(DrawReason));
    }
    internal void PlayedCompleted(ProgramSkillFrame f)
    {
        if(!Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.PlayedCompleted,Response:{}action,Played:{}plan}r||action.ActionId!=r.ResponseActionId)throw new InvalidOperationException("Pingliao lost its exact completed native Played receipt.");
        events.AdvanceFact(new MouSmReplyEvent(f.Id,r.Cursor,r.Actor,true));frames.ReplaceTop(f with{MouSimaYi=r with{Stage=MouSmStage.PlayedChildren,Cursor=r.Cursor+1,PlayedSeats=r.PlayedSeats.Append(r.Actor).ToArray(),After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});Continue(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouSimaYiPingliao)]private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(!Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.Drawing,Draw:null}r||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=2)throw new InvalidOperationException("Pingliao lost its exact once-issued Draw2.");
        frames.ReplaceTop(f with{MouSimaYi=r with{Stage=MouSmStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouSmCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouSimaYi=null,PendingMovementContinuation=null});native.Finish(f);}
    [ProgramContinuation(ProgramContinuationKind.Resume,36200)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouSimaYi:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,36200)]private bool Movement(ProgramSkillFrame f)=>f.MouSimaYi is not null&&Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36200)]private void Invariant(ProgramSkillFrame f){if(f.MouSimaYi is not null&&!Valid(f))throw new InvalidOperationException("Invalid Mou Sima Yi native receipt.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36200)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36200)]private bool Paid(ProgramSkillFrame f)=>f.MouSimaYi is not null&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.InitializeMouSimaYiQuanmou,SkillProgramEffectOp.ResolveMouSimaYiQuanmou,SkillProgramEffectOp.ResolveMouSimaYiPingliao,SkillProgramEffectOp.SpreadMouSimaYiQuanmouDamage)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;if(r.Stage is MouSmStage.Target or MouSmStage.SpreadTargets)return p.Choices.Where(c=>c.Targets.Count==1).OrderByDescending(c=>facts.DamageValue(f.OwnerSeat,c.Targets[0])).FirstOrDefault()??p.Choices.Last();
        if(r.Stage==MouSmStage.Give)return p.Choices.OrderBy(c=>facts.Keep(p.PlayerSeat,c.Cards.Single())).First();
        return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="decline")??p.Choices.First();
    }
}
