using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule,ProgramCapability]
internal sealed class MouCaoAngRuntime(IMouCaoAngFacts facts,IMouCaoAngNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts,IProgramOperationDraws draws):IMouCaoAngRuntime
{
    internal const string Fengmin="mou:fengmin",Zhiwang="mou:zhiwang",DiscardReason="skill-program.mou:zhiwang.equipment-discard";
    internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    internal bool Used(int owner,string instance)=>facts.History<MouCaFengminPaidEvent>().Any(e=>e.Owner==owner&&e.Instance==instance&&e.Turn==facts.Turn&&e.TurnOwner==facts.TurnOwner);
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);if(f.MouCaoAng is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.InstructionIndex!=1||!facts.Parent(f)||facts.Program(f.SkillId)?.GameplayHash!=f.GameplayHash)throw new InvalidOperationException("Mou Cao Ang requires the exact original native trigger parent.");
        if(op==SkillProgramEffectOp.ResolveMouCaoAngFengmin&&Used(f.OwnerSeat,f.SkillInstanceId))return SkillProgramStepOutcome.Continue;
        var r=new MouCaoAngReceipt(Source(f),f.GameplayHash,op,facts.Turn,facts.TurnOwner,f.WindowContext!.ParentFrameId){Before=facts.Sequence,After=facts.Sequence};
        if(op==SkillProgramEffectOp.ResolveMouCaoAngFengmin)
        {
            var loser=facts.LostEquipmentOwner(f);if(loser<0)throw new InvalidOperationException("Fengmin lost the actual equipment departure owner.");
            r=r with{Stage=MouCaoAngStage.Drawing,Loser=loser,Requested=facts.EmptySlots(loser)};
        }
        else if(op==SkillProgramEffectOp.ResolveMouCaoAngZhiwang)r=r with{Stage=MouCaoAngStage.DiscardChildren,Equipment=facts.Equipment(f.OwnerSeat)};
        else throw new InvalidOperationException("Unknown Mou Cao Ang instruction.");
        frames.ReplaceTop(f=f with{MouCaoAng=r});events.AdvanceFact(new MouCaStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.TurnOwner,r.ParentId));
        if(op==SkillProgramEffectOp.ResolveMouCaoAngFengmin)
        {
            events.AdvanceFact(new MouCaFengminPaidEvent(f.Id,f.OwnerSeat,f.SkillInstanceId,r.Turn,r.TurnOwner,r.ParentId,r.Loser,r.Requested));
            if(r.Requested==0){Complete(f);return SkillProgramStepOutcome.AwaitChild;}
            draws.BeginOperationProgramDraw(f,f.OwnerSeat,r.Requested,new("skill-program.mou:fengmin.draw"));return SkillProgramStepOutcome.AwaitChild;
        }
        if(r.Equipment.Count==0){Complete(f);return SkillProgramStepOutcome.AwaitChild;}
        frames.ReplaceTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Discard(f);Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)=>f.MouCaoAng is{}r&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId)?.GameplayHash==f.GameplayHash&&r.Turn==facts.Turn&&r.TurnOwner==facts.TurnOwner&&r.ParentId==f.WindowContext?.ParentFrameId&&f.InstructionIndex==1&&facts.Parent(f)&&facts.History<MouCaStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner&&e.ParentId==r.ParentId)==1;
    private int Chooser(ProgramSkillFrame f)=>f.MouCaoAng!.Stage==MouCaoAngStage.UseChoice?f.MouCaoAng.Actor:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouCaoAng!;Dictionary<string,string> Args(string branch,int index)=>new(){{"program-action","mou-cao-ang"},{"branch",branch},{"index",index.ToString(CultureInfo.InvariantCulture)}};
        if(r.Stage==MouCaoAngStage.Material)return facts.Pairs(f).Select((p,i)=>new PromptChoice(new($"mou-ca.{f.Id}.pair.{i}"),$"令{facts.Name(p.Actor)}使用弃牌堆的【{p.Name}】",[p.CardId],[p.Actor],Args("pair",i))).ToArray();
        return facts.Plans(f).Select((a,i)=>new PromptChoice(new($"mou-ca.{f.Id}.use.{i}"),a.Description,[r.CardId],a.TargetSeats,Args("use",i))).ToArray();
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices=Choices(f);var who=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new(DecisionKind.ProgramTrigger,who,f.MouCaoAng!.Stage==MouCaoAngStage.Material?"质亡：选择仍在弃牌堆的伤害牌和有合法用牌方案的其他角色。":"质亡：为这张牌选择真实合法目标并使用。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=true,Choices=choices},facts.Human(who));native.Publish();
    }
    [ProgramAnswer("mou-cao-ang")]private void Answer(PromptChoice choice)
    {
        if(frames.Top is not ProgramSkillFrame{MouCaoAng:{}r}f||!Valid(f)||prompts.Current is not{Kind:DecisionKind.ProgramTrigger}p||p.PlayerSeat!=Chooser(f)||Choices(f).SingleOrDefault(c=>c.Id==choice.Id)is not{}canonical||!canonical.Cards.SequenceEqual(choice.Cards)||!canonical.Targets.SequenceEqual(choice.Targets)||canonical.Parameters.Count!=choice.Parameters.Count||canonical.Parameters.Any(x=>choice.Parameters.GetValueOrDefault(x.Key)!=x.Value))throw new InvalidOperationException("Zhiwang requires the actual chooser's current canonical material, actor and native targets.");
        var index=int.Parse(choice.Parameters["index"],CultureInfo.InvariantCulture);prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==MouCaoAngStage.Material){var selected=facts.Pairs(f)[index];frames.ReplaceTop(f with{MouCaoAng=r with{Stage=MouCaoAngStage.UseChoice,Actor=selected.Actor,CardId=selected.CardId}});Continue(f.Id);return;}
        var action=facts.Plans(f)[index];var n=new MouCaNativeUse(f.Id,f.InstructionIndex,r.Source,r.GameplayHash,r.Turn,r.TurnOwner,r.Actor,r.CardId,action.PlayedCardKind??facts.Kind(r.CardId),action,0,0){Targets=action.TargetSeats};
        frames.ReplaceTop(f=f with{MouCaoAng=r with{Stage=MouCaoAngStage.IssuingUse,Native=n}});native.Use(f);
    }
    internal void Continue(long id)
    {
        while(prompts.Current is null&&frames.Top is ProgramSkillFrame{MouCaoAng:{}r}f&&f.Id==id)
        {
            if(!Valid(f))throw new InvalidOperationException("Mou Cao Ang lost his owning issued native receipt.");if(facts.Finished){Complete(f);return;}
            if(r.Stage is MouCaoAngStage.Material or MouCaoAngStage.UseChoice){if(!facts.Alive(Chooser(f))||Choices(f).Count==0){Complete(f);return;}Publish(f);return;}
            if(r.Stage is MouCaoAngStage.Drawing or MouCaoAngStage.IssuingUse or MouCaoAngStage.NativeUse)return;
            if(r.Stage==MouCaoAngStage.ReturnedUse){Complete(f);return;}
            if(native.Drain(f))return;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
            if(r.Stage==MouCaoAngStage.DrawChildren){Complete(f);return;}
            if(r.ActualDiscard==0||!facts.Alive(f.OwnerSeat)){Complete(f);return;}
            frames.ReplaceTop(f with{MouCaoAng=r with{Stage=MouCaoAngStage.Material}});
        }
    }
    internal void Returned(MouCaNativeUse n,bool activeAttack)
    {
        if(frames.Find(n.FrameId)is not ProgramSkillFrame{MouCaoAng:{Stage:MouCaoAngStage.NativeUse,Native:{}current}r}f||!Valid(f)||!SameUse(n,current)||facts.History<MouCaUseReturnedEvent>().Any(e=>e.UseId==n.UseId))throw new InvalidOperationException("Zhiwang requires its one exact completed native Use return.");
        frames.Replace(f.Id,f with{MouCaoAng=r with{Stage=MouCaoAngStage.ReturnedUse}});events.AdvanceFact(new MouCaUseReturnedEvent(f.Id,n.UseId,n.ActionId));if(!activeAttack)frames.AdvanceProgram(f.Id);
    }
    internal static bool SameAction(LegalAction a,LegalAction b)=>a.Kind==b.Kind&&a.CardId==b.CardId&&a.PlayedCardKind==b.PlayedCardKind&&a.TargetSeat==b.TargetSeat&&a.TargetCardId==b.TargetCardId&&a.ConversionSource==b.ConversionSource&&a.TargetSeats.SequenceEqual(b.TargetSeats)&&(a.AdditionalConversionSources??[]).SequenceEqual(b.AdditionalConversionSources??[])&&a.ProgramSkillId==b.ProgramSkillId&&a.ProgramActivationId==b.ProgramActivationId;
    internal static bool SameUse(MouCaNativeUse a,MouCaNativeUse b)=>(a.FrameId,a.Instruction,a.Source,a.GameplayHash,a.Turn,a.TurnOwner,a.Actor,a.CardId,a.Kind,a.UseId,a.ActionId)==(b.FrameId,b.Instruction,b.Source,b.GameplayHash,b.Turn,b.TurnOwner,b.Actor,b.CardId,b.Kind,b.UseId,b.ActionId)&&a.Targets.SequenceEqual(b.Targets)&&SameAction(a.Action,b.Action);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouCaoAngFengmin)]private void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome o)
    {
        if(!Valid(f)||f.MouCaoAng is not{Stage:MouCaoAngStage.Drawing,Draw:null}r||o.RecipientSeat!=f.OwnerSeat||o.RequestedCount!=r.Requested)throw new InvalidOperationException("Fengmin draw must return its actual empty-slot count once.");
        frames.Replace(f.Id,f with{MouCaoAng=r with{Stage=MouCaoAngStage.DrawChildren,Draw=o,Before=o.SequenceBefore,After=o.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouCaDrawReturnedEvent(f.Id,r.Requested,o.ActualDrawIds.Count,o.ReplacementGainedIds.Count,o.AttemptFrameId));Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouCaCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouCaoAng=null,PendingMovementContinuation=null});native.Finish(f);}
    [ProgramContinuation(ProgramContinuationKind.Resume,36700)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouCaoAng:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,36700)]private bool Movement(ProgramSkillFrame f)=>f.MouCaoAng is not null&&Resume(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,36700)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,36700)]private bool Paid(ProgramSkillFrame f)=>f.MouCaoAng is not null&&Valid(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,36700)]private void Invariant(ProgramSkillFrame f){if(f.MouCaoAng is not null&&!Valid(f))throw new InvalidOperationException("Invalid own-frame Mou Cao Ang receipt.");}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouCaoAngFengmin,SkillProgramEffectOp.ResolveMouCaoAngZhiwang)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderByDescending(c=>c.Targets.Sum(t=>facts.TargetValue(Chooser(f),t))).First();
}
