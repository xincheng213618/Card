using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule,ProgramCapability]
internal sealed partial class MouSunShangxiangRuntime(IMouSunShangxiangFacts facts,IMouSunShangxiangNative native,
    IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws,
    IProgramHealthChildQueries health,IProgramHealthChildOperations healthOperations):IMouSunShangxiangHost
{
    internal const string Jieyin="mou-mobile:jieyin",Liangzhu="mou-mobile:liangzhu",Xiaoji="mou-mobile:xiaoji";
    internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    internal int Holder(int owner)=>facts.History<MouSsxZhuChangedEvent>().LastOrDefault(e=>e.OwnerSeat==owner)?.CurrentSeat??-1;
    internal bool Failed(int owner)=>facts.History<MouSsxMissionFailedEvent>().Any(e=>e.OwnerSeat==owner);
    private bool RepeatHolder(int owner,int seat)=>facts.History<MouSsxZhuChangedEvent>().Count(e=>e.OwnerSeat==owner&&e.CurrentSeat==seat)>1;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,35100)]
    internal bool Qualifies(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>c.SkillId switch {
        Jieyin=>!Failed(c.OwnerSeat)&&(context.Window==SkillProgramTriggerWindow.GameStarting?!facts.History<MouSsxZhuChangedEvent>().Any(e=>e.OwnerSeat==c.OwnerSeat):
            context.Window==SkillProgramTriggerWindow.CharacterDied?context.TargetSeat==Holder(c.OwnerSeat):Holder(c.OwnerSeat)>=0),
        Xiaoji=>facts.Faction(c.OwnerSeat)=="wu",_=>true};
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition,35100)]
    internal bool Activation(CharacterState owner,SkillProgramActivation a)=>a.Effects.All(e=>e.Op!=SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu)||
        facts.Faction(owner.Seat)=="shu"&&facts.Field(owner.Seat,true).Count>0;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(f.MouSsx is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.InstructionIndex!=1||!facts.ParentMatches(f))throw new InvalidOperationException("Sun Shangxiang requires the exact actual lifecycle or own Play entry.");
        var holder=Holder(f.OwnerSeat);var stage=op==SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu?MouSsxStage.DepositChoice:
            op==SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji?MouSsxStage.Drawing:f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting?MouSsxStage.InitialHolder:MouSsxStage.HolderChoice;
        var r=new MouSsxReceipt(Source(f),f.GameplayHash,op,frames.ParentOf(f.Id)?.Id??0){Stage=stage,HolderSeat=holder,Before=facts.MovementSequence,After=facts.MovementSequence};
        frames.ReplaceTop(f=f with{MouSsx=r});events.AdvanceFact(new MouSsxStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentFrameId,holder));
        if(op==SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji){BeginDraw(f,f.OwnerSeat);return SkillProgramStepOutcome.AwaitChild;}
        if(op==SkillProgramEffectOp.ResolveMouSunShangxiangJieyin&&stage!=MouSsxStage.InitialHolder&&(holder<0||!facts.Alive(holder))){Failure(f);return SkillProgramStepOutcome.AwaitChild;}
        Publish(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private static PromptChoice Choice(ProgramSkillFrame f,string branch,string label,int value=-1,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>new(
        new($"mou-ssx.{f.Id}.{branch}.{value}"),label,cards??[],targets??[],new Dictionary<string,string>{{"program-action","mou-ssx"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
    private int Chooser(ProgramSkillFrame f)=>f.MouSsx is{Stage:MouSsxStage.HolderChoice or MouSsxStage.GiftCards or MouSsxStage.BenefitChoice,HolderSeat:>=0}r?r.HolderSeat:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouSsx!;var choices=new List<PromptChoice>();
        switch(r.Stage)
        {
            case MouSsxStage.InitialHolder:case MouSsxStage.TransferHolder:
                foreach(var seat in facts.Living.Where(s=>s!=f.OwnerSeat&&s!=r.HolderSeat))choices.Add(Choice(f,"holder","令"+facts.Name(seat)+"获得助",seat,targets:[seat]));
                if(r.Stage==MouSsxStage.TransferHolder)choices.Add(Choice(f,"remove","移除助，使命失败"));break;
            case MouSsxStage.HolderChoice:
                if(facts.Hand(r.HolderSeat).Count>0)choices.Add(Choice(f,"gift","交给孙尚香至多两张手牌，然后获得1点护甲"));
                choices.Add(Choice(f,"move",RepeatHolder(f.OwnerSeat,r.HolderSeat)?"令其移除助标记":"令其移动或移除助标记"));break;
            case MouSsxStage.GiftCards:
                if(r.Selected.Count<r.Required)foreach(var card in facts.Hand(r.HolderSeat).Where(c=>!r.Selected.Contains(c.Id)))choices.Add(Choice(f,"card","选择交出的手牌",card.Id,[card.Id]));
                if(r.Selected.Count==r.Required)choices.Add(Choice(f,"confirm","交出所选手牌",cards:r.Selected));break;
            case MouSsxStage.DepositChoice:
                foreach(var entity in facts.Field(f.OwnerSeat,true))choices.Add(Choice(f,"deposit","将"+facts.Name(entity.From.OwnerSeat!.Value)+"的【"+CardCatalog.Get(entity.Kind).DisplayName+"】置为妆",entity.CardId,[entity.CardId],[entity.From.OwnerSeat.Value]));break;
            case MouSsxStage.BenefitChoice:choices.Add(Choice(f,"recover","回复1点体力"));choices.Add(Choice(f,"draw","摸两张牌"));break;
            case MouSsxStage.FieldChoice:
                foreach(var entity in facts.Field(f.OwnerSeat,false))choices.Add(Choice(f,"discard","弃置"+facts.Name(entity.From.OwnerSeat!.Value)+"场上的【"+CardCatalog.Get(entity.Kind).DisplayName+"】",entity.CardId,[entity.CardId],[entity.From.OwnerSeat.Value]));
                choices.Add(Choice(f,"decline","不弃置场上的牌"));break;
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void Publish(ProgramSkillFrame f)
    {
        if(!facts.Alive(f.OwnerSeat)||facts.GameOver){Finish(f);return;}
        var r=f.MouSsx!;
        if(r.Stage==MouSsxStage.BenefitChoice&&!facts.Alive(r.HolderSeat)){Finish(f);return;}
        var choices=Choices(f);if(choices.Count==0){Finish(f);return;}
        var chooser=Chooser(f);var name=f.SkillId==Jieyin?"结姻":f.SkillId==Liangzhu?"良助":"枭姬";
        frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new(DecisionKind.ProgramTrigger,chooser,name+"：请选择。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){
            PromptId=prompts.NextId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,name,name,name)},facts.Human(chooser));native.Publish();
    }
    private static bool Equal(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(p=>b.Parameters.GetValueOrDefault(p.Key)==p.Value);
    [ProgramAnswer("mou-ssx")]
    internal void Answer(PromptChoice supplied)
    {
        if(frames.Top is not ProgramSkillFrame{MouSsx:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||!Choices(f).Any(c=>Equal(c,supplied)))throw new InvalidOperationException("Sun Shangxiang requires the exact actual private holder or owner choice.");
        prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        switch(supplied.Parameters["branch"])
        {
            case "holder":native.Mark(f,r.HolderSeat,supplied.Targets.Single(),r.Stage==MouSsxStage.InitialHolder);Finish(f);return;
            case "gift":frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.GiftCards,Required=Math.Min(2,facts.Hand(r.HolderSeat).Count),Selected=[]}});Publish(f);return;
            case "card":frames.ReplaceTop(f=f with{MouSsx=r with{Selected=r.Selected.Append(supplied.Cards.Single()).ToArray()}});Publish(f);return;
            case "confirm":
                if(r.Selected.Count!=r.Required||r.Required<1||r.Selected.Any(id=>facts.Location(id)!=CardLocation.Hand(r.HolderSeat)))throw new InvalidOperationException("Jieyin lost its actual holder's exact Hand payment.");
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.GiftChildren,Paid=true,Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(r.HolderSeat,0,null)});
                events.AdvanceFact(new MouSsxGiftCommittedEvent(f.Id,r.HolderSeat,r.Required));native.Give(f,r.HolderSeat,r.Selected);Seal(f.Id);Continue(f.Id);return;
            case "move":
                if(RepeatHolder(f.OwnerSeat,r.HolderSeat)){Failure(f);return;}
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.TransferHolder}});Publish(f);return;
            case "remove":Failure(f);return;
            case "deposit":
                var deposited=facts.Field(f.OwnerSeat,true).Single(e=>e.CardId==supplied.Cards.Single());
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.DepositChildren,Paid=true,Entities=[deposited],Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                native.Deposit(f,deposited);Seal(f.Id);Continue(f.Id);return;
            case "recover":
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.RecoverChildren,RecipientSeat=r.HolderSeat,Paid=true}});
                events.AdvanceFact(new MouSsxBenefitIssuedEvent(f.Id,r.HolderSeat,true));native.Recover(f,r.HolderSeat);if(frames.Top?.Id==f.Id)Continue(f.Id);return;
            case "draw":events.AdvanceFact(new MouSsxBenefitIssuedEvent(f.Id,r.HolderSeat,false));BeginDraw(f,r.HolderSeat);return;
            case "discard":
                var field=facts.Field(f.OwnerSeat,false).Single(e=>e.CardId==supplied.Cards.Single());
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.DiscardChildren,Paid=true,Entities=[field],Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(field.From.OwnerSeat!.Value,0,null)});
                native.Discard(f,field);Seal(f.Id);Continue(f.Id);return;
            case "decline":Finish(f);return;
            default:throw new InvalidOperationException("Unknown Sun Shangxiang choice.");
        }
    }
    private void Failure(ProgramSkillFrame f)
    {
        var r=f.MouSsx!;if(Failed(f.OwnerSeat))throw new InvalidOperationException("Jieyin cannot fail and pay its mission twice.");
        frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.FailureRecoverChildren,Paid=true,RecipientSeat=f.OwnerSeat}});
        native.Mark(f,r.HolderSeat,-1,false);events.AdvanceFact(new MouSsxMissionFailedEvent(f.Id,f.OwnerSeat,r.HolderSeat));
        events.AdvanceFact(new MouSsxFailureStepEvent(f.Id,1));native.Recover(f,f.OwnerSeat);if(frames.Top?.Id==f.Id)Continue(f.Id);
    }
    private void Seal(long id)
    {var f=frames.ActiveProgram(id);frames.ReplaceTop(f with{MouSsx=f.MouSsx! with{After=facts.MovementSequence}});}
    private void BeginDraw(ProgramSkillFrame f,int recipient)
    {
        if(!facts.Alive(recipient)||facts.GameOver){Finish(f);return;}
        frames.ReplaceTop(f=f with{MouSsx=f.MouSsx! with{Stage=MouSsxStage.Drawing,Paid=true,RecipientSeat=recipient,Required=2,DrawReturned=false},PendingMovementContinuation=null});
        draws.BeginOperationProgramDraw(f,recipient,2,new(f.SkillId==Xiaoji?"skill-program.mou-mobile:xiaoji.draw":"skill-program.mou-mobile:liangzhu.draw"));
    }
    internal void Continue(long id)
    {
        if(frames.Top is not ProgramSkillFrame{MouSsx:{}r}f||f.Id!=id)return;
        if(!Valid(f))throw new InvalidOperationException("Sun Shangxiang lost her actual owning continuation.");
        if(r.Stage is MouSsxStage.InitialHolder or MouSsxStage.HolderChoice or MouSsxStage.GiftCards or MouSsxStage.TransferHolder or MouSsxStage.DepositChoice or MouSsxStage.BenefitChoice or MouSsxStage.FieldChoice){Publish(f);return;}
        if(r.Stage==MouSsxStage.Drawing)return;
        if(native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouSsx!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
        if(!facts.Alive(f.OwnerSeat)||facts.GameOver){Finish(f);return;}
        switch(r.Stage)
        {
            case MouSsxStage.GiftChildren:if(facts.Alive(r.HolderSeat))native.Armor(f,r.HolderSeat);Finish(frames.ActiveProgram(id));return;
            case MouSsxStage.DepositChildren:
                if(r.HolderSeat<0||!facts.Alive(r.HolderSeat)){Finish(f);return;}
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.BenefitChoice}});Publish(f);return;
            case MouSsxStage.FailureRecoverChildren:
                var entities=facts.Zhuang(f.OwnerSeat);frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.FailureObtainChildren,Entities=entities,Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
                events.AdvanceFact(new MouSsxFailureStepEvent(f.Id,2));native.ObtainZhuang(f,entities);Seal(f.Id);Continue(f.Id);return;
            case MouSsxStage.FailureObtainChildren:
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.FailureFaction}});events.AdvanceFact(new MouSsxFailureStepEvent(f.Id,3));native.Faction(f);
                f=frames.ActiveProgram(id);frames.ReplaceTop(f=f with{MouSsx=f.MouSsx! with{Stage=MouSsxStage.FailureMaximumHpChildren}});events.AdvanceFact(new MouSsxFailureStepEvent(f.Id,4));native.MaximumHp(f);Continue(f.Id);return;
            case MouSsxStage.DrawChildren when r.Operation==SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji:
                frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.FieldChoice}});Publish(f);return;
            default:Finish(f);return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu,SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji)]
    internal void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.MouSsx is not{Stage:MouSsxStage.Drawing,DrawReturned:false}r||outcome.RequestedCount!=2||outcome.RecipientSeat!=r.RecipientSeat)throw new InvalidOperationException("Sun Shangxiang cannot replace or replay the paid native draw.");
        frames.ReplaceTop(f=f with{MouSsx=r with{Stage=MouSsxStage.DrawChildren,DrawReturned=true,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(outcome.RecipientSeat,0,null)});
        events.AdvanceFact(new MouSsxDrawReturnedEvent(f.Id,outcome.RecipientSeat,2,outcome.ActualDrawIds.Count));Continue(f.Id);
    }
    private void Finish(ProgramSkillFrame f)
    {events.AdvanceFact(new MouSsxCompletedEvent(f.Id,f.MouSsx!.Operation));frames.ReplaceTop(f=f with{MouSsx=null,PendingMovementContinuation=null});native.Finish(f);}
    internal bool Valid(ProgramSkillFrame f)=>f.MouSsx is{}r&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId).GameplayHash==f.GameplayHash&&
        f.InstructionIndex==1&&r.ParentFrameId==(frames.ParentOf(f.Id)?.Id??0)&&facts.ParentMatches(f)&&r.Before<=r.After&&r.After<=facts.MovementSequence&&r.Selected.Distinct().Count()==r.Selected.Count&&
        facts.History<MouSsxStartedEvent>().Count(e=>e==new MouSsxStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ParentFrameId,r.HolderSeat))==1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,35100)]
    internal void Assert(ProgramSkillFrame f){if(f.MouSsx is not null&&!Valid(f))throw new InvalidOperationException("Sun Shangxiang lost her original source, actual parent or paid entities.");}
    [ProgramContinuation(ProgramContinuationKind.Resume,35100)]
    internal bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouSsx:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,35100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,35100)]
    internal bool Preserve(ProgramSkillFrame f)=>f.MouSsx is{Paid:true}&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouSunShangxiangJieyin,SkillProgramEffectOp.ResolveMouSunShangxiangLiangzhu,SkillProgramEffectOp.ResolveMouSunShangxiangXiaoji)]
    internal PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>f.MouSsx!.Stage switch {
        MouSsxStage.InitialHolder or MouSsxStage.TransferHolder=>p.Choices.OrderByDescending(c=>c.Targets.Count==1?facts.HelpValue(f.OwnerSeat,c.Targets[0]):double.MinValue).First(),
        MouSsxStage.HolderChoice=>p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="gift")??p.Choices.Last(),
        MouSsxStage.GiftCards=>p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="confirm")??p.Choices.OrderBy(c=>facts.KeepValue(p.PlayerSeat,c.Cards.Single())).First(),
        MouSsxStage.BenefitChoice=>p.Choices.Last(),
        MouSsxStage.FieldChoice=>p.Choices.FirstOrDefault(c=>c.Targets.Count==1&&facts.HelpValue(f.OwnerSeat,c.Targets[0])<0)??p.Choices.Last(),
        _=>p.Choices.First()};
}
