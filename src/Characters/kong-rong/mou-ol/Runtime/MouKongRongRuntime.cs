using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MouKongRongRuntime(IMouKongRongFacts facts, IMouKongRongNative native,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts,
    IProgramOperationDraws draws, IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations) : IMouKongRongHost
{
    internal const string Liwen = "mou-ol:liwen", Zhengyi = "mou-ol:zhengyi", DrawReason = "skill-program.mou-ol:liwen.decline-draw";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 34600)]
    internal bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if(c.SkillId == Zhengyi) return context.TargetSeat is {} target && facts.Xian(target)>0 && facts.Living.Any(s=>s!=target&&facts.Xian(s)>0);
        if(c.SkillId != Liwen || context.Window is SkillProgramTriggerWindow.GameStarting or SkillProgramTriggerWindow.AfterTurnEnded) return true;
        return context.CardUse is {} use && facts.History<MouKrActionObservedEvent>().Any(e=>e.ActionId==use.CardActionId&&e.ActorSeat==c.OwnerSeat&&e.MatchesPrevious&&e.Source?.SkillInstanceId==c.SkillInstanceId)&&
            !facts.History<MouKrXianEarnedEvent>().Any(e=>e.OwnerSeat==c.OwnerSeat&&e.ActionId==use.CardActionId);
    }
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(op==SkillProgramEffectOp.PayMouKongRongZhengyi)
        {
            if(f.MouKrZhengyi is not{Stage:MouKrZhengyiStage.Committed}r||!Valid(f))throw new InvalidOperationException("Zhengyi lost the committed private ballot.");
            if(r.Prevent)native.PreventDamage(f);
            frames.ReplaceTop(f=f with{MouKrZhengyi=r with{Stage=MouKrZhengyiStage.Paying,Cursor=0}});Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        if(f.MouKrLiwen is not null||f.MouKrZhengyi is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.InstructionIndex!=1||!facts.ParentMatches(f))throw new InvalidOperationException("Kong Rong lost the exact native triggering parent.");
        if(op==SkillProgramEffectOp.ResolveMouKongRongZhengyi)
        {
            var start=facts.Damage(f)??throw new InvalidOperationException("Zhengyi requires the actual positive normal damage.");
            var eligible=facts.Living.Where(s=>s!=start.VictimSeat&&facts.Xian(s)>0).ToArray();
            if(facts.Xian(start.VictimSeat)<=0||eligible.Length==0)throw new InvalidOperationException("Zhengyi lost its actual marked participants.");
            frames.ReplaceTop(f=f with{MouKrZhengyi=new(start.Source,start.GameplayHash,start.WindowId,start.AttackId,start.VictimSeat,start.Amount){Eligible=eligible}});
            events.AdvanceFact(start);Publish(f);return SkillProgramStepOutcome.AwaitChild;
        }
        if(f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting)
        { var actual=native.ChangeXian(f,f.OwnerSeat,3);events.AdvanceFact(new MouKrXianEarnedEvent(f.Id,f.OwnerSeat,0,actual));return SkillProgramStepOutcome.Continue; }
        if(f.WindowContext.Window is SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted)
        {
            var action=facts.Action(f)??throw new InvalidOperationException("Liwen lost its completed original actual action.");
            if(!facts.History<MouKrActionObservedEvent>().Any(e=>e.ActionId==action.ActionId&&e.ActorSeat==f.OwnerSeat&&e.MatchesPrevious))throw new InvalidOperationException("Liwen cannot award an unmatched action.");
            events.AdvanceFact(new MouKrXianEarnedEvent(f.Id,f.OwnerSeat,action.ActionId,native.ChangeXian(f,f.OwnerSeat,1)));return SkillProgramStepOutcome.Continue;
        }
        if(f.WindowContext.Window!=SkillProgramTriggerWindow.AfterTurnEnded)throw new InvalidOperationException("Liwen requires an actual turn end.");
        var receipt=new MouKrLiwenReceipt(Source(f),f.GameplayHash,frames.ParentOf(f.Id)!.Id){AllocationBudget=facts.Xian(f.OwnerSeat),Before=facts.MovementSequence,After=facts.MovementSequence};
        frames.ReplaceTop(f=f with{MouKrLiwen=receipt});events.AdvanceFact(new MouKrLiwenStartedEvent(f.Id,receipt.Source,receipt.GameplayHash,receipt.WindowId));Publish(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private static PromptChoice Choice(ProgramSkillFrame f,string branch,string description,int value=-1,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>new(
        new($"mou-kr.{f.Id}.{branch}.{value}"),description,cards??[],targets??[],new Dictionary<string,string>{{"program-action","mou-kr"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch},{"value",value.ToString(CultureInfo.InvariantCulture)}});
    private int Actor(ProgramSkillFrame f)=>f.MouKrZhengyi is{}z?z.Eligible[z.Votes.Count]:f.MouKrLiwen is{Stage:MouKrLiwenStage.UseChoice or MouKrLiwenStage.UseDraft}r?r.Schedule[r.Cursor].Seat:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        if(f.MouKrZhengyi is{Stage:MouKrZhengyiStage.Voting})return [Choice(f,"agree","同意失去体力"),Choice(f,"refuse","不同意")];
        var r=f.MouKrLiwen!;var result=new List<PromptChoice>();
        if(r.Stage==MouKrLiwenStage.Allocate)
        {
            if(r.Recipients.Count<r.AllocationBudget&&facts.Xian(f.OwnerSeat)>0)foreach(var seat in facts.Living.Where(s=>facts.Xian(s)<5&&!r.Recipients.Contains(s)))
                result.Add(Choice(f,"allocate","分配1个贤给"+facts.Name(seat),seat,targets:[seat]));
            result.Add(Choice(f,"scheduled","结束分配，依贤数量使用牌"));
        }
        else if(r.Stage==MouKrLiwenStage.UseChoice)
        {
            result.AddRange(facts.Actions(f,Actor(f)).Select((a,i)=>Choice(f,"action",a.Description,i,a.CardId is>0?[a.CardId.Value]:[],a.TargetSeats)));
            result.Add(Choice(f,"decline","不使用，移去全部贤并令孔融摸等量牌"));
        }
        else if(r.Stage==MouKrLiwenStage.UseDraft&&r.SelectedAction is{}a)
        {
            if(r.Cards.Count<a.MaxCardCount)foreach(var id in a.SelectableCardIds.Where(id=>!r.Cards.Contains(id)))result.Add(Choice(f,"material","选择使用素材",id,[id]));
            if(r.Targets.Count<a.MaxTargetCount)foreach(var seat in a.SelectableTargetSeats.Where(s=>!r.Targets.Contains(s)))result.Add(Choice(f,"target","选择目标"+facts.Name(seat),seat,targets:[seat]));
            if(facts.DraftValid(a,r.Cards,r.Targets))result.Add(Choice(f,"use","确认使用",cards:r.Cards,targets:r.Targets));
            result.Add(Choice(f,"back","重新选择牌"));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void Publish(ProgramSkillFrame f)
    {
        var actor=Actor(f);var choices=Choices(f);
        frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        var name=f.MouKrZhengyi is null?"立文":"争义";
        prompts.Set(new(DecisionKind.ProgramTrigger,actor,name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){
            PromptId=prompts.NextId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,name,name,name)},facts.Human(actor));native.Publish();
    }
    private static bool Same(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(p=>b.Parameters.GetValueOrDefault(p.Key)==p.Value);
    [ProgramAnswer("mou-kr")]
    internal void Answer(PromptChoice supplied)
    {
        if(frames.Top is not ProgramSkillFrame f||!Valid(f)||prompts.Current?.PlayerSeat!=Actor(f)||!Choices(f).Any(c=>Same(c,supplied)))throw new InvalidOperationException("Kong Rong requires the exact currently published private answer.");
        var actor=Actor(f);var branch=supplied.Parameters["branch"];var original=prompts.Current!;prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(f.MouKrZhengyi is{}z)
        {
            var votes=z.Votes.Append(new MouKrVote(actor,branch=="agree")).ToArray();
            frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Votes=votes}});
            if(votes.Length<z.Eligible.Count){Publish(f);return;}
            var consent=votes.Where(v=>v.Agree&&facts.Alive(v.Seat)).ToArray();var max=consent.Select(v=>facts.Hp(v.Seat)).DefaultIfEmpty(int.MinValue).Max();
            var payers=consent.Where(v=>facts.Hp(v.Seat)==max).Select(v=>new MouKrScheduledSeat(v.Seat,max)).ToArray();
            frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Stage=MouKrZhengyiStage.Committed,Prevent=payers.Length>0,Payers=payers,Cursor=0}});
            // Nothing about a ballot is emitted until every participant has committed.
            foreach(var item in votes.Select((v,i)=>(v,i)))events.AdvanceFact(new MouKrZhengyiVoteEvent(f.Id,item.i,item.v.Seat,item.v.Agree));
            foreach(var item in payers.Select((v,i)=>(v,i)))events.AdvanceFact(new MouKrZhengyiPayerEvent(f.Id,item.i,item.v.Seat,item.v.Count));
            events.AdvanceFact(new MouKrZhengyiCommittedEvent(f.Id,z.Prevent,payers.Length));
            frames.AdvanceProgram(f.Id);return;
        }
        var r=f.MouKrLiwen!;
        switch(branch)
        {
            case "allocate":
                var seat=supplied.Targets.Single();native.ChangeXian(f,f.OwnerSeat,-1);native.ChangeXian(f,seat,1);
                frames.ReplaceTop(f=f with{MouKrLiwen=r with{Recipients=r.Recipients.Append(seat).ToArray()}});
                events.AdvanceFact(new MouKrXianAllocatedEvent(f.Id,r.Recipients.Count,seat));Publish(f);return;
            case "scheduled":
                var schedule=facts.Living.Where(s=>facts.Xian(s)>0).Select(s=>new MouKrScheduledSeat(s,facts.Xian(s))).OrderByDescending(s=>s.Count).ThenBy(s=>s.Seat).ToArray();
                frames.ReplaceTop(f=f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.UseChoice,Schedule=schedule,Cursor=0}});
                foreach(var item in schedule.Select((v,i)=>(v,i)))events.AdvanceFact(new MouKrLiwenScheduledEvent(f.Id,item.i,item.v.Seat,item.v.Count));Continue(f.Id);return;
            case "decline":
                var removed=-native.ChangeXian(f,actor,-facts.Xian(actor));events.AdvanceFact(new MouKrLiwenDeclinedEvent(f.Id,r.Cursor,actor,removed));
                frames.ReplaceTop(f=f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.DrawChildren,RequestedDraw=removed,DrawReturned=false,Before=facts.MovementSequence,After=facts.MovementSequence}});
                if(removed>0&&facts.Alive(f.OwnerSeat)&&!facts.GameOver)draws.BeginOperationProgramDraw(f,f.OwnerSeat,removed,new(DrawReason));else Next(f);return;
            case "action":
                var action=facts.Actions(f,actor)[int.Parse(supplied.Parameters["value"],CultureInfo.InvariantCulture)];
                frames.ReplaceTop(f=f with{MouKrLiwen=r with{SelectedAction=action,Cards=[],Targets=[],OriginalDecision=original,Stage=MouKrLiwenStage.UseDraft}});
                if(action.MaxCardCount==0&&action.MaxTargetCount==0)Issue(f);else Publish(f);return;
            case "material":frames.ReplaceTop(f=f with{MouKrLiwen=r with{Cards=r.Cards.Append(supplied.Cards.Single()).ToArray()}});Publish(f);return;
            case "target":frames.ReplaceTop(f=f with{MouKrLiwen=r with{Targets=r.Targets.Append(supplied.Targets.Single()).ToArray()}});Publish(f);return;
            case "back":frames.ReplaceTop(f=f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.UseChoice,SelectedAction=null,Cards=[],Targets=[]}});Publish(f);return;
            case "use":frames.ReplaceTop(f=f with{MouKrLiwen=r with{OriginalDecision=original}});Issue(f);return;
            default:throw new InvalidOperationException("Unknown Kong Rong answer.");
        }
    }
    private void Issue(ProgramSkillFrame f)
    {
        var r=f.MouKrLiwen!;var actor=r.Schedule[r.Cursor].Seat;var action=r.SelectedAction!;
        if(!facts.DraftValid(action,r.Cards,r.Targets)||!facts.Actions(f,actor).Any(a=>System.Text.Json.JsonSerializer.Serialize(a)==System.Text.Json.JsonSerializer.Serialize(action)))throw new InvalidOperationException("Liwen lost its live native action or paid material selection.");
        frames.ReplaceTop(f=f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.Issuing,Before=facts.MovementSequence,After=facts.MovementSequence,UseId=0,ActionId=0,NativeReturned=false}});
        native.IssueUse(f,actor,action,r.Cards,r.Targets);
    }
    internal void Continue(long id)
    {
        if(frames.Top is not ProgramSkillFrame f||f.Id!=id||!Valid(f))return;
        if(f.MouKrZhengyi is{}z)
        {
            if(z.Stage==MouKrZhengyiStage.HealthChildren)
            {
                if(native.Drain(f))return;f=frames.ActiveProgram(id);z=f.MouKrZhengyi!;
                var last=z.Payers[z.Cursor-1].Seat;
                if(!facts.History<ProgramSkillHpLostEvent>().Any(e=>e.FrameId==f.Id&&e.TargetSeat==last&&e.Amount==z.Amount))
                { native.LoseHp(f,last,z.Amount);if(frames.Top?.Id!=id)return;if(native.Drain(f))return; }
                frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Stage=MouKrZhengyiStage.Paying}});
            }
            if(z.Stage!=MouKrZhengyiStage.Paying)return;
            while(z.Cursor<z.Payers.Count&&!facts.GameOver)
            {
                var seat=z.Payers[z.Cursor].Seat;var index=z.Cursor;
                frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Cursor=index+1,Stage=MouKrZhengyiStage.HealthChildren}});
                if(!facts.Alive(seat)){frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Stage=MouKrZhengyiStage.Paying}});continue;}
                events.AdvanceFact(new MouKrZhengyiHpIssuedEvent(f.Id,index,seat,z.Amount));native.LoseHp(f,seat,z.Amount);
                if(frames.Top?.Id!=id||native.Drain(f))return;
                frames.ReplaceTop(f=f with{MouKrZhengyi=z=z with{Stage=MouKrZhengyiStage.Paying}});
            }
            events.AdvanceFact(new MouKrZhengyiCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouKrZhengyi=null});native.Finish(f);return;
        }
        var r=f.MouKrLiwen!;
        if(r.Stage is MouKrLiwenStage.Issuing or MouKrLiwenStage.NativeUse)return;
        if(r.Stage is MouKrLiwenStage.Children or MouKrLiwenStage.DrawChildren)
        {
            if(r.Stage==MouKrLiwenStage.DrawChildren&&!r.DrawReturned)return;
            if(native.Drain(f))return;Next(frames.ActiveProgram(id));return;
        }
        if(r.Stage==MouKrLiwenStage.Allocate){Publish(f);return;}
        while(r.Cursor<r.Schedule.Count&&!facts.Alive(r.Schedule[r.Cursor].Seat)){frames.ReplaceTop(f=f with{MouKrLiwen=r=r with{Cursor=r.Cursor+1}});}
        if(r.Cursor>=r.Schedule.Count||facts.GameOver){events.AdvanceFact(new MouKrLiwenCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouKrLiwen=null,PendingMovementContinuation=null});native.Finish(f);return;}
        Publish(f);
    }
    private void Next(ProgramSkillFrame f)
    {
        var r=f.MouKrLiwen!;frames.ReplaceTop(f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.UseChoice,Cursor=r.Cursor+1,SelectedAction=null,Cards=[],Targets=[],UseId=0,ActionId=0,NativeReturned=false,Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=null});Continue(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouKongRongLiwen)]
    internal void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.MouKrLiwen is not{Stage:MouKrLiwenStage.DrawChildren,DrawReturned:false}r||outcome.RecipientSeat!=f.OwnerSeat||outcome.RequestedCount!=r.RequestedDraw)throw new InvalidOperationException("Liwen cannot repeat or replace its refusal draw.");
        frames.ReplaceTop(f=f with{MouKrLiwen=r with{DrawReturned=true,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        events.AdvanceFact(new MouKrLiwenDrawReturnedEvent(f.Id,r.Cursor,r.RequestedDraw,outcome.ActualDrawIds.Count));Continue(f.Id);
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if(facts.Program(f.SkillId).GameplayHash!=f.GameplayHash||!facts.ParentMatches(f))return false;
        if(f.MouKrLiwen is{}r)return f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&r.WindowId==frames.ParentOf(f.Id)?.Id&&
            r.Recipients.Count<=r.AllocationBudget&&r.Recipients.Distinct().Count()==r.Recipients.Count&&r.Cursor>=0&&r.Cursor<=r.Schedule.Count&&r.Before<=r.After&&r.After<=facts.MovementSequence&&
            facts.History<MouKrLiwenStartedEvent>().Count(e=>e==new MouKrLiwenStartedEvent(f.Id,r.Source,r.GameplayHash,r.WindowId))==1;
        if(f.MouKrZhengyi is{}z)return f.InstructionIndex is>=1 and<=3&&z.Source==Source(f)&&z.GameplayHash==f.GameplayHash&&z.WindowId==frames.ParentOf(f.Id)?.Id&&z.Amount>0&&
            z.Eligible.Distinct().Count()==z.Eligible.Count&&z.Votes.Count<=z.Eligible.Count&&z.Votes.Select(v=>v.Seat).SequenceEqual(z.Eligible.Take(z.Votes.Count))&&z.Payers.Select(p=>p.Seat).Distinct().Count()==z.Payers.Count&&z.Cursor>=0&&z.Cursor<=z.Payers.Count&&
            facts.History<MouKrZhengyiStartedEvent>().Count(e=>e==new MouKrZhengyiStartedEvent(f.Id,z.Source,z.GameplayHash,z.WindowId,z.AttackId,z.VictimSeat,z.Amount))==1;
        return false;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,34600)]
    internal void Assert(ProgramSkillFrame f){if((f.MouKrLiwen is not null||f.MouKrZhengyi is not null)&&!Valid(f))throw new InvalidOperationException("Kong Rong lost his exact native frame, source hash or paid cursor.");}
    [ProgramContinuation(ProgramContinuationKind.Resume,34600)]
    internal bool Resume(long id)
    {
        if(frames.Top is not ProgramSkillFrame f||f.Id!=id||f.MouKrLiwen is null&&f.MouKrZhengyi is null||f.MouKrZhengyi is{Stage:MouKrZhengyiStage.Committed})return false;
        Continue(id);return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34600)]
    internal bool Preserve(ProgramSkillFrame f)=>(f.MouKrLiwen is{}r&&(r.Recipients.Count>0||r.Stage!=MouKrLiwenStage.Allocate)||f.MouKrZhengyi is{Stage:not MouKrZhengyiStage.Voting})&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouKongRongLiwen,SkillProgramEffectOp.ResolveMouKongRongZhengyi)]
    internal PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>f.MouKrZhengyi is{}z?
        p.Choices.First(c=>c.Parameters["branch"]==(facts.Friendly(p.PlayerSeat,z.VictimSeat)&&facts.Hp(p.PlayerSeat)>z.Amount?"agree":"refuse")):
        p.Choices.FirstOrDefault(c=>c.Parameters["branch"] is "scheduled" or "use")??p.Choices.FirstOrDefault(c=>c.Parameters["branch"] is "action" or "material" or "target")??p.Choices.Last();
}
