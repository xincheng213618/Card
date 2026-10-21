using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouZhuRanRuntime(IMouZhuRanFacts facts,IMouZhuRanNative native,
    IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouZhuRanHost
{
    internal const string Zhenwei="mou-mobile:zhenwei",Heyuan="mou-mobile:heyuan",Action="mou-zhu-ran";
    internal const string CostReason="skill-program.mou-mobile:zhenwei.discard",HeyuanCostReason="skill-program.mou-mobile:heyuan.discard",
        DrawReason="skill-program.mou-mobile:zhenwei.draw",HeyuanDrawReason="skill-program.mou-mobile:heyuan.draw";
    private static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    private static bool Z(ProgramSkillFrame f)=>f.MouZhuRan?.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei;
    private MouZhuRanResolvedEvent? Last(int owner)
    {
        var started=facts.History<MouZhuRanStartedEvent>().LastOrDefault(e=>e.Source.OwnerSeat==owner&&e.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei);
        return started is null?null:facts.History<MouZhuRanResolvedEvent>().SingleOrDefault(e=>e.FrameId==started.FrameId&&e.OwnerSeat==owner);
    }
    private bool UsedTarget(int owner,int target)=>facts.History<MouZhuRanHeyuanAcceptedEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.TargetSeat==target);
    private IReadOnlyList<int> Beneficiaries(int owner)=>facts.Seats.Where(s=>facts.Player(s)is{Alive:true,Wounded:true}&&!UsedTarget(owner,s)).ToArray();
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,34400,SkillProgramEffectOp.ResolveMouZhuRanZhenwei)]
    private bool CanActivate(ProgramActivationLegalityContext c)=>facts.Phase is{Kind:ActualDiscardRecoveryPhaseKind.Play}phase&&phase.ActorSeat==c.OwnerSeat&&
        !facts.History<MouZhuRanStartedEvent>().Any(e=>e.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei&&e.Source.OwnerSeat==c.OwnerSeat&&e.Phase==phase);
    [ProgramPhaseUsedProjection(34400)]
    private int UsedPhase(int owner,string skill,int fallback)=>skill==Zhenwei&&facts.Phase is{}phase?
        Math.Max(fallback,facts.History<MouZhuRanStartedEvent>().Count(e=>e.Operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei&&e.Source.OwnerSeat==owner&&e.Phase==phase)):fallback;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection,34400,SkillProgramEffectOp.ResolveMouZhuRanHeyuan)]
    private bool CanTrigger(ProgramTriggerLegalityContext c)=>Last(c.OwnerSeat)is{LastEffect:not null}last&&
        facts.Discardable(c.OwnerSeat).Count>=last.DiscardCount&&Beneficiaries(c.OwnerSeat).Count>0;
    public SkillProgramStepOutcome Start(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(f.MouZhuRan is not null||!facts.ExactStart(f,operation))throw new InvalidOperationException("Mou Zhu Ran requires his exact actual Play or own Ending binding.");
        var opponent=operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei?f.SelectedTargetSeats.Single():-1;
        var r=new MouZhuRanReceipt(Source(f),f.GameplayHash,operation,f.InstructionIndex,facts.Turn,
            operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei?facts.Phase:null,f.WindowContext?.ParentFrameId,opponent);
        if(operation==SkillProgramEffectOp.ResolveMouZhuRanZhenwei)
        {
            var supporters=facts.History<MouZhuRanHeyuanAcceptedEvent>().Where(e=>e.Source.OwnerSeat==f.OwnerSeat)
                .Select(e=>e.TargetSeat).Where(s=>s!=opponent&&s!=f.OwnerSeat&&facts.Player(s).Alive);
            var participants=facts.ActionOrder(f.OwnerSeat,new[]{f.OwnerSeat,opponent}.Concat(supporters));
            r=r with{Stage=MouZhuRanStage.Commit,Participants=participants};
        }
        else
        {
            if(Last(f.OwnerSeat)is not{LastEffect:{}lastEffect}last||facts.Discardable(f.OwnerSeat).Count<last.DiscardCount||Beneficiaries(f.OwnerSeat).Count==0)
                throw new InvalidOperationException("Heyuan requires the latest Zhenwei's actual final item and an unused wounded beneficiary.");
            r=r with{Stage=MouZhuRanStage.HeyuanTarget,X=last.DiscardCount,PreviousFrame=last.FrameId,Effects=[lastEffect]};
        }
        frames.ReplaceTop(f=f with{MouZhuRan=r});
        events.AdvanceFact(new MouZhuRanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Instruction,r.ActualTurn,r.Phase,r.WindowId,r.Opponent));
        Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if(f.MouZhuRan is not{}r||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash||r.GameplayHash!=f.GameplayHash||r.Source!=Source(f)||r.Instruction!=f.InstructionIndex||
            !Enum.IsDefined(r.Stage)||r.Cursor<0||r.Cursor>r.Participants.Count||r.EffectIndex<0||r.EffectIndex>r.Effects.Count||r.X<0||
            r.Participants.Distinct().Count()!=r.Participants.Count||r.Votes.Select(v=>v.Seat).Distinct().Count()!=r.Votes.Count||
            r.Selected.Select(c=>c.CardId).Distinct().Count()!=r.Selected.Count||r.Paying.Select(c=>c.CardId).Distinct().Count()!=r.Paying.Count||
            r.Effects.Distinct().Count()!=r.Effects.Count||r.Effects.Any(e=>!Enum.IsDefined(e))||r.Before<0||r.After<r.Before||r.After>facts.MovementSequence||
            facts.History<MouZhuRanStartedEvent>().Count(e=>e==new MouZhuRanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Instruction,r.ActualTurn,r.Phase,r.WindowId,r.Opponent))!=1)return false;
        if(Z(f))return f.SkillId==Zhenwei&&f.ActivationId=="sealed-discard"&&f.TriggerId is null&&f.WindowContext is null&&
            r.Phase is{Kind:ActualDiscardRecoveryPhaseKind.Play}p&&p.ActorSeat==f.OwnerSeat&&f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.SequenceEqual(new[]{r.Opponent})&&
            r.Participants.Contains(f.OwnerSeat)&&r.Participants.Contains(r.Opponent)&&r.Votes.Count<=r.Participants.Count&&r.Effects.Count<=2;
        return r.Operation==SkillProgramEffectOp.ResolveMouZhuRanHeyuan&&f.SkillId==Heyuan&&f.ActivationId==f.TriggerId&&f.TriggerId=="repeat-last"&&
            f.SelectedCardIds.Count==0&&f.SelectedTargetSeats.Count==0&&r.WindowId==f.WindowContext?.ParentFrameId&&facts.NativeParent(f)&&r.Effects.Count==1&&
            facts.History<MouZhuRanResolvedEvent>().Any(e=>e.FrameId==r.PreviousFrame&&e.OwnerSeat==f.OwnerSeat&&e.DiscardCount==r.X&&e.LastEffect==r.Effects[0]);
    }
    private int Chooser(ProgramSkillFrame f)=>f.MouZhuRan is{Stage:MouZhuRanStage.Commit}r?r.Participants[r.Cursor]:f.OwnerSeat;
    private PromptChoice Option(ProgramSkillFrame f,string branch,string label,int? card=null,int? target=null)=>new(
        new ChoiceId($"mou-zhu-ran.{f.Id}.{f.MouZhuRan!.Stage}.{f.MouZhuRan.Cursor}.{branch}.{card}.{target}"),label,card is{}id?[id]:[],target is{}s?[s]:[],
        new Dictionary<string,string>{{"program-action",Action},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
    internal IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouZhuRan!;
        if(r.Stage is MouZhuRanStage.Commit or MouZhuRanStage.HeyuanCost)
        {
            var chooser=Chooser(f);var cards=facts.Discardable(chooser);
            var result=cards.Where(c=>r.Selected.Any(s=>s.CardId==c.CardId)||r.Stage==MouZhuRanStage.Commit||r.Selected.Count<r.X)
                .Select(c=>Option(f,"card",$"{(r.Selected.Any(s=>s.CardId==c.CardId)?"取消":"选择")}弃置【{facts.CardLabel(c.CardId)}】",c.CardId)).ToList();
            if(r.Stage==MouZhuRanStage.Commit||r.Selected.Count==r.X)result.Add(Option(f,"confirm",$"确认弃置{r.Selected.Count}张牌"));
            if(r.Stage==MouZhuRanStage.HeyuanCost)result.Add(Option(f,"decline","不发动合援"));
            return result;
        }
        if(r.Stage==MouZhuRanStage.HeyuanTarget)return Beneficiaries(f.OwnerSeat).Select(s=>Option(f,"target",s==f.OwnerSeat?"对自己发动合援":$"对座位{s+1}发动合援",target:s))
            .Append(Option(f,"decline","不发动合援")).ToArray();
        if(r.Stage==MouZhuRanStage.Effects)
        {
            var options=new List<PromptChoice>();
            if(r.Effects.Count<r.X)
            {
                if(!r.Effects.Contains(MouZhuRanEffect.Damage))options.Add(Option(f,"damage","先执行：对镇围对象造成1点伤害"));
                if(!r.Effects.Contains(MouZhuRanEffect.Draw))options.Add(Option(f,"draw","先执行：摸三张牌"));
            }
            options.Add(Option(f,"confirm",$"按所选顺序执行{r.Effects.Count}项"));return options;
        }
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var chooser=Chooser(f);var choices=Choices(f);
        frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,chooser,f.MouZhuRan!.Stage switch{
            MouZhuRanStage.Commit=>"镇围：各自密选弃牌，全部提交后才支付。",MouZhuRanStage.Effects=>"镇围：选择至多X项不同效果，按选定顺序执行。",
            MouZhuRanStage.HeyuanTarget=>"合援：选择尚未帮助过的已受伤角色。",_=>"合援：选择所需数量的真实手牌或装备牌。"},
            choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
            {PromptId=prompts.NextId(),IsPrivate=true,TargetSeat=chooser,Choices=Array.AsReadOnly(choices.ToArray())},facts.Player(chooser).Human);
        native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if(frames.Top is not ProgramSkillFrame{MouZhuRan:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||
            Choices(f).SingleOrDefault(c=>c.Id==chosen.Id)is not{}canonical||!canonical.Cards.SequenceEqual(chosen.Cards)||!canonical.Targets.SequenceEqual(chosen.Targets)||
            canonical.Parameters.Count!=chosen.Parameters.Count||canonical.Parameters.Any(p=>chosen.Parameters.GetValueOrDefault(p.Key)!=p.Value))
            throw new InvalidOperationException("Mou Zhu Ran requires the current chooser's exact private commitment.");
        prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=chosen.Parameters["branch"];
        if(branch=="decline"){Complete(f);return;}
        if(r.Stage is MouZhuRanStage.Commit or MouZhuRanStage.HeyuanCost)
        {
            if(branch=="card")
            {
                var card=facts.Discardable(Chooser(f)).Single(c=>c.CardId==chosen.Cards.Single());
                frames.ReplaceTop(f with{MouZhuRan=r with{Selected=r.Selected.Any(c=>c.CardId==card.CardId)?r.Selected.Where(c=>c.CardId!=card.CardId).ToArray():r.Selected.Append(card).ToArray()}});
            }
            else
            {
                var legal=facts.Discardable(Chooser(f));
                if(r.Selected.Any(c=>!legal.Any(l=>l.CardId==c.CardId&&l.From==c.From)))throw new InvalidOperationException("Committed discard materials must still be real discardable HE.");
                if(r.Stage==MouZhuRanStage.Commit)frames.ReplaceTop(f with{MouZhuRan=r with{Votes=r.Votes.Append(new(r.Participants[r.Cursor]){Cards=r.Selected}).ToArray(),Cursor=r.Cursor+1,Selected=[]}});
                else AcceptHeyuan(f);
            }
        }
        else if(r.Stage==MouZhuRanStage.HeyuanTarget)frames.ReplaceTop(f with{MouZhuRan=r with{Recipient=chosen.Targets.Single(),Stage=MouZhuRanStage.HeyuanCost}});
        else if(r.Stage==MouZhuRanStage.Effects)frames.ReplaceTop(f with{MouZhuRan=branch=="confirm"?r with{Stage=MouZhuRanStage.Execute}:
            r with{Effects=r.Effects.Append(branch=="damage"?MouZhuRanEffect.Damage:MouZhuRanEffect.Draw).ToArray()}});
        Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouZhuRan:{}r}f&&f.Id==id)
        {
            if(!Valid(f))throw new InvalidOperationException("Mou Zhu Ran lost the original source, owning frame or paid native cursor.");
            if(facts.Finished){Complete(f);return;}
            if(r.Stage is MouZhuRanStage.Commit or MouZhuRanStage.HeyuanCost or MouZhuRanStage.HeyuanTarget or MouZhuRanStage.Effects)
            {
                if(r.Stage==MouZhuRanStage.Commit&&r.Cursor>=r.Participants.Count)
                {
                    frames.ReplaceTop(f with{MouZhuRan=r with{Stage=MouZhuRanStage.Pay,Accepted=true,Cursor=0,Selected=[]}});
                    events.AdvanceFact(new MouZhuRanCommittedEvent(f.Id,r.Votes.Count));continue;
                }
                if(r.Stage==MouZhuRanStage.Commit&&!facts.Player(r.Participants[r.Cursor]).Alive)
                {frames.ReplaceTop(f with{MouZhuRan=r with{Votes=r.Votes.Append(new(r.Participants[r.Cursor])).ToArray(),Cursor=r.Cursor+1,Selected=[]}});continue;}
                Publish(f);return;
            }
            if(r.Stage==MouZhuRanStage.Pay)
            {
                if(r.Cursor>=r.Votes.Count)
                {
                    if(Z(f))Compare(f);else frames.ReplaceTop(f with{MouZhuRan=r with{Stage=MouZhuRanStage.Execute}});
                    continue;
                }
                var vote=r.Votes[r.Cursor];var legal=facts.Discardable(vote.Seat);
                var actual=facts.Player(vote.Seat).Alive?vote.Cards.Select(c=>legal.SingleOrDefault(l=>l.CardId==c.CardId&&l.From==c.From)).OfType<MouZhuRanCard>().ToArray():[];
                if(actual.Length==0){frames.ReplaceTop(f with{MouZhuRan=r with{Cursor=r.Cursor+1}});continue;}
                var before=facts.MovementSequence;
                frames.ReplaceTop(f=f with{MouZhuRan=r with{Stage=MouZhuRanStage.CostChildren,Payer=vote.Seat,Paying=actual,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                native.Discard(f,vote.Seat,actual);if(frames.Top?.Id!=id)return;continue;
            }
            if(r.Stage==MouZhuRanStage.CostChildren)
            {
                if(native.Drain(f))return;
                frames.ReplaceTop(f with{MouZhuRan=r with{Stage=MouZhuRanStage.Pay,Cursor=r.Cursor+1,Paying=[]},PendingMovementContinuation=null});continue;
            }
            if(r.Stage==MouZhuRanStage.Execute)
            {
                if(r.EffectIndex>=r.Effects.Count){Complete(f);return;}
                var effect=r.Effects[r.EffectIndex];var recipient=Z(f)?effect==MouZhuRanEffect.Damage?r.Opponent:f.OwnerSeat:r.Recipient;
                if(!facts.Player(recipient).Alive){frames.ReplaceTop(f with{MouZhuRan=r with{EffectIndex=r.EffectIndex+1}});continue;}
                frames.ReplaceTop(f=f with{MouZhuRan=r with{Recipient=recipient,Draw=null,Stage=effect==MouZhuRanEffect.Damage?MouZhuRanStage.Damage:MouZhuRanStage.Drawing}});
                events.AdvanceFact(new MouZhuRanEffectIssuedEvent(f.Id,r.EffectIndex,effect,f.OwnerSeat,recipient));
                if(effect==MouZhuRanEffect.Damage)native.Damage(f,recipient);else draws.BeginOperationProgramDraw(f,recipient,3,new(Z(f)?DrawReason:HeyuanDrawReason));
                if(frames.Top?.Id!=id)return;continue;
            }
            if(r.Stage==MouZhuRanStage.Drawing)return;
            if(r.Stage is MouZhuRanStage.Damage or MouZhuRanStage.DrawChildren)
            {
                if(f.AttackAttempt is not null||native.Drain(f))return;
                frames.ReplaceTop(f with{MouZhuRan=r with{LastEffect=r.Effects[r.EffectIndex],EffectIndex=r.EffectIndex+1,Stage=MouZhuRanStage.Execute},PendingMovementContinuation=null});continue;
            }
        }
    }
    private void Compare(ProgramSkillFrame f)
    {
        var r=f.MouZhuRan!;var paid=facts.History<MouZhuRanCostPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        var owner=paid.Where(e=>e.Payer!=r.Opponent).ToArray();var other=paid.Where(e=>e.Payer==r.Opponent).ToArray();
        var ownerSuits=owner.Select(e=>e.Suit).Distinct().Count();var otherSuits=other.Select(e=>e.Suit).Distinct().Count();
        var x=(owner.Length>=other.Length?1:0)+(ownerSuits>=otherSuits?1:0);
        frames.ReplaceTop(f with{MouZhuRan=r with{Stage=MouZhuRanStage.Effects,X=x,OwnerCount=owner.Length,OwnerSuits=ownerSuits,OpponentCount=other.Length,OpponentSuits=otherSuits}});
        events.AdvanceFact(new MouZhuRanComparisonEvent(f.Id,owner.Length,ownerSuits,other.Length,otherSuits,x));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhuRanZhenwei,SkillProgramEffectOp.ResolveMouZhuRanHeyuan)]
    private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(!Valid(f)||f.MouZhuRan is not{Stage:MouZhuRanStage.Drawing}r||outcome.RecipientSeat!=r.Recipient||outcome.RequestedCount!=3)
            throw new InvalidOperationException("Zhenwei/Heyuan must return its exact recipient's native Draw3, including a zero-actual replacement.");
        frames.ReplaceTop(f=f with{MouZhuRan=r with{Stage=MouZhuRanStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        events.AdvanceFact(new MouZhuRanDrawReturnedEvent(f.Id,outcome.RecipientSeat,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        if(Z(f))events.AdvanceFact(new MouZhuRanResolvedEvent(f.Id,f.OwnerSeat,f.MouZhuRan!.OwnerCount,f.MouZhuRan.LastEffect));
        events.AdvanceFact(new MouZhuRanCompletedEvent(f.Id));
        frames.ReplaceTop(f=f with{MouZhuRan=null,PendingMovementContinuation=null});native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,34400)]
    private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouZhuRan:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,34400)]
    private bool Movement(ProgramSkillFrame f)=>f.MouZhuRan is not null&&Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,34400)]
    private void Invariant(ProgramSkillFrame f){if(f.MouZhuRan is not null&&!Valid(f))throw new InvalidOperationException("Invalid Mou Zhu Ran owning receipt.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34400)]
    private bool Paid(ProgramSkillFrame f)=>f.MouZhuRan is{Accepted:true}&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouZhuRanZhenwei,SkillProgramEffectOp.ResolveMouZhuRanHeyuan)]
    private PromptChoice Ai(PendingDecision d,ProgramSkillFrame f)
    {
        var r=f.MouZhuRan!;
        if(r.Stage is MouZhuRanStage.Commit or MouZhuRanStage.HeyuanCost)
        {
            var want=r.Stage==MouZhuRanStage.HeyuanCost?r.X:d.PlayerSeat==r.Opponent?0:1;
            if(r.Selected.Count>=want)return d.Choices.First(c=>c.Parameters["branch"]=="confirm");
            var card=d.Choices.Where(c=>c.Cards is[var id]&&!r.Selected.Any(s=>s.CardId==id)).OrderBy(c=>facts.KeepValue(d.PlayerSeat,c.Cards.Single())).FirstOrDefault();
            return card??d.Choices.First(c=>c.Parameters["branch"] is "confirm" or "decline");
        }
        if(r.Stage==MouZhuRanStage.Effects)return d.Choices.FirstOrDefault(c=>c.Parameters["branch"]==(r.Effects.Count==0&&!facts.Friendly(f.OwnerSeat,r.Opponent)?"damage":"draw"))??d.Choices.Last();
        return d.Choices.FirstOrDefault(c=>c.Targets is[var seat]&&facts.Friendly(f.OwnerSeat,seat))??d.Choices.Last();
    }
}
