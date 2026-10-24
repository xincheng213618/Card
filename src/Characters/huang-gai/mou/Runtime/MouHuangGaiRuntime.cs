using System.Globalization;
namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouHuangGaiRuntime(IMouHuangGaiFacts facts,IMouHuangGaiNative native,IProgramRuntimeFrames frames,IProgramRuntimeEvents events,IProgramRuntimePrompts prompts):IMouHuangGaiRuntime
{
    internal const string Lieji="mou:lieji",Quzhou="mou:quzhou",RevealBind="quzhou-reveal",ObtainReason="skill-program.mou:quzhou.obtain",DiscardReason="skill-program.mou:quzhou.unusable-slash";
    private static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    private static bool IsSlash(CardKind k)=>k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash;
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(op==SkillProgramEffectOp.ArmMouHuangGaiLieji)
        {
            if(!facts.LiejiParent(f)||facts.History<MouHgLiejiGrantedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Lieji requires its original completed actual Trick use.");
            var grants=facts.DamageHand(f.OwnerSeat).Select(id=>new MouHgLiejiGrant(f.Id,Source(f),f.GameplayHash,facts.Turn,facts.TurnOwner,id)).ToArray();
            native.SetGrants(f.OwnerSeat,facts.Grants(f.OwnerSeat).Concat(grants).ToArray());
            events.AdvanceFact(new MouHgLiejiGrantedEvent(f.Id,Source(f),facts.Turn,facts.TurnOwner));return SkillProgramStepOutcome.Continue;
        }
        if(f.MouHuangGai is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.SkillId!=Quzhou||f.ActivationId!="reveal-and-sail"||f.TriggerId is not null||f.InstructionIndex!=1||!facts.OwnPlay(f.OwnerSeat)||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)throw new InvalidOperationException("Quzhou requires its original actual Play entry.");
        var r=new MouHgReceipt(Source(f),f.GameplayHash,facts.Turn,facts.TurnOwner){Stage=MouHgStage.Reveal};frames.ReplaceTop(f=f with{MouHuangGai=r});
        events.AdvanceFact(new MouHgStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner));Reveal(f);return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,35400)]
    private void BindBonuses(CardUseDeclaredEvent declared)
    {
        if(facts.Use(declared.ResolutionId)is not{Materials.Count:>0}u)return;
        var grants=facts.Seats().SelectMany(facts.Grants).Where(g=>g.Turn==facts.Turn&&g.TurnOwner==facts.TurnOwner&&u.Materials.Contains(g.CardId)).ToArray();
        if(grants.Length==0)return;if(u.Bonuses is not null)throw new InvalidOperationException("Lieji cannot issue its material benefits twice.");
        native.SetUseBonuses(u.UseId,grants.Select(g=>new MouHgUseBonus(u.UseId,u.ActionId,g)).ToArray());
        foreach(var g in grants)events.AdvanceFact(new MouHgBonusIssuedEvent(u.UseId,u.ActionId,g.FrameId,g.CardId));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,35401)]
    private void Expire(TurnEndedEvent ended)
    {
        foreach(var seat in facts.Seats())
        {
            var grants=facts.Grants(seat);
            var left=grants.Where(g=>g.Turn!=ended.TurnNumber||g.TurnOwner!=ended.ActorSeat).ToArray();native.SetGrants(seat,left.Length>0?left:null);
        }
    }
    internal int DamageBonus(MouHgUseFacts? use)
    {
        if(use is not{Bonuses:{}bonuses}u)return 0;
        return bonuses.Count(b=>b.UseId==u.UseId&&b.ActionId==u.ActionId&&u.Materials.Contains(b.Grant.CardId)&&b.Grant.Turn==facts.Turn&&b.Grant.TurnOwner==facts.TurnOwner&&
            facts.Program(b.Grant.Source.SkillId)?.GameplayHash==b.Grant.GameplayHash&&facts.History<MouHgBonusIssuedEvent>().Count(e=>e.UseId==u.UseId&&e.ActionId==u.ActionId&&e.GrantFrameId==b.Grant.FrameId&&e.CardId==b.Grant.CardId)==1);
    }
    internal bool Valid(ProgramSkillFrame f)=>f.MouHuangGai is{}r&&f.SkillId==Quzhou&&f.ActivationId=="reveal-and-sail"&&f.TriggerId is null&&f.InstructionIndex==1&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&
        facts.Program(f.SkillId)?.GameplayHash==r.GameplayHash&&r.Turn==facts.Turn&&r.TurnOwner==facts.TurnOwner&&r.Before<=r.After&&r.After<=facts.Sequence&&r.Reveals.Select(c=>c.CardId).Distinct().Count()==r.Reveals.Count&&
        facts.History<MouHgStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner)==1;
    private void Reveal(ProgramSkillFrame f)
    {
        var before=facts.Sequence;var bind=RevealBind+"-"+f.MouHuangGai!.Reveals.Count.ToString(CultureInfo.InvariantCulture);
        frames.ReplaceTop(f=f with{MouHuangGai=f.MouHuangGai with{Stage=MouHgStage.RevealChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var shown=native.Reveal(f,bind);f=frames.ActiveProgram(f.Id);var r=f.MouHuangGai!;
        if(shown is not null){r=r with{Reveals=r.Reveals.Append(shown).ToArray()};events.AdvanceFact(new MouHgRevealedEvent(f.Id,r.Reveals.Count-1,shown.CardId,shown.Kind,shown.Sequence));}
        frames.ReplaceTop(f=f with{MouHuangGai=r with{After=facts.Sequence,RevealExhausted=shown is null}});Continue(f.Id);
    }
    private static PromptChoice Choice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int>? targets=null)=>new(new($"mou-hg.{f.Id}.{branch}.{string.Join('-',targets??[])}"),label,[],targets??[],new Dictionary<string,string>{{"program-action","mou-hg"},{"branch",branch},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouHuangGai!;
        if(r.Stage==MouHgStage.Target)return Array.AsReadOnly(facts.Targets(f).Select(seat=>Choice(f,"slash","对"+facts.Name(seat)+"使用亮出的杀",[seat])).ToArray());
        if(r.Stage!=MouHgStage.Choice)return [];
        var choices=new List<PromptChoice>{Choice(f,"obtain","获得所有亮出的牌")};
        if(r.Reveals.Count-1<facts.LivingCount)choices.Add(Choice(f,"again","再次亮出牌堆顶一张牌"));return choices.AsReadOnly();
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices=Choices(f);if(choices.Count==0){Obtain(f);return;}
        frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger,f.OwnerSeat,"趋舟：请选择。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=prompts.NextId(),IsPrivate=false,Choices=choices,SkillPrompt=facts.Presentation(f.SkillId)},facts.Human(f.OwnerSeat));native.Publish();
    }
    [ProgramAnswer("mou-hg")]
    private void Answer(PromptChoice supplied)
    {
        if(frames.Top is not ProgramSkillFrame{MouHuangGai:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=f.OwnerSeat||!Choices(f).Any(c=>facts.SameChoice(c,supplied)))throw new InvalidOperationException("Quzhou requires the exact currently published choice.");
        prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        switch(supplied.Parameters["branch"])
        {
            case"again":Reveal(f);break;
            case"obtain":Obtain(f);break;
            case"slash":frames.ReplaceTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.NativeUse,Before=facts.Sequence,After=facts.Sequence}});native.Slash(f,supplied.Targets.Single());break;
            default:throw new InvalidOperationException("Unknown Quzhou choice.");
        }
    }
    private void Obtain(ProgramSkillFrame f)
    {
        var ids=facts.ProcessingReveals(f);frames.ReplaceTop(f=f with{MouHuangGai=f.MouHuangGai! with{Stage=MouHgStage.ObtainChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(ids.Count>0)native.Obtain(f,ids);f=frames.ActiveProgram(f.Id);frames.ReplaceTop(f with{MouHuangGai=f.MouHuangGai! with{After=facts.Sequence}});Continue(f.Id);
    }
    internal void MovesPaid(long id,IReadOnlyList<CardMovementRecord> moves)
    {
        var f=frames.ActiveProgram(id);frames.Replace(id,f with{MouHuangGai=f.MouHuangGai! with{After=facts.Sequence}});
        foreach(var m in moves)events.AdvanceFact(new MouHgMovedEvent(id,m.CardId,m.From,m.To,m.Sequence));
    }
    private void DiscardSlash(ProgramSkillFrame f)
    {
        frames.ReplaceTop(f=f with{MouHuangGai=f.MouHuangGai! with{Stage=MouHgStage.DiscardSlashChildren,Before=facts.Sequence,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        native.DiscardSlash(f);f=frames.ActiveProgram(f.Id);frames.ReplaceTop(f with{MouHuangGai=f.MouHuangGai! with{After=facts.Sequence}});Continue(f.Id);
    }
    internal void UseReturned(MouHgNativeUse origin,bool attackActive)
    {
        var f=frames.ActiveProgram(origin.FrameId);frames.ReplaceTop(f with{MouHuangGai=f.MouHuangGai! with{NativeReturned=true}});if(!attackActive)SettleUse(origin);
    }
    internal void SettleUse(MouHgNativeUse origin)
    {
        var f=frames.ActiveProgram(origin.FrameId);var r=f.MouHuangGai!;
        if(r.Native!=origin||!r.NativeReturned||facts.HasNativeUse(origin.UseId)||facts.History<MouHgNativeReturnedEvent>().Any(e=>e.Origin==origin))throw new InvalidOperationException("Quzhou cannot return its original use twice.");
        frames.ReplaceTop(f with{MouHuangGai=r with{Stage=MouHgStage.NativeChildren,After=facts.Sequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});events.AdvanceFact(new MouHgNativeReturnedEvent(origin));Continue(f.Id);
    }
    internal void Continue(long id)
    {
        if(frames.Top is not ProgramSkillFrame{MouHuangGai:{}r}f||f.Id!=id)return;
        if(!Valid(f))throw new InvalidOperationException("Quzhou lost its actual original owning continuation.");
        if(r.Stage is MouHgStage.Choice or MouHgStage.Target){Publish(f);return;}if(r.Stage==MouHgStage.NativeUse)return;
        if(native.Drain(id))return;f=frames.ActiveProgram(id);r=f.MouHuangGai!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
        if(facts.Finished||!facts.Alive(f.OwnerSeat)){Finish(f);return;}
        if(r.Stage==MouHgStage.RevealChildren)
        {
            if(r.RevealExhausted){Obtain(f);return;}
            if(r.Reveals.Count==0){Finish(f);return;}
            if(IsSlash(r.Reveals[^1].Kind))
            {
                if(facts.Targets(f).Count==0){DiscardSlash(f);return;}
                frames.ReplaceTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.Target}});Publish(f);return;
            }
            frames.ReplaceTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.Choice}});Publish(f);return;
        }
        if(r.Stage is MouHgStage.NativeChildren or MouHgStage.DiscardSlashChildren){Obtain(f);return;}Finish(f);
    }
    private void Finish(ProgramSkillFrame f){events.AdvanceFact(new MouHgCompletedEvent(f.Id,f.MouHuangGai!.Reveals.Count));frames.ReplaceTop(f=f with{MouHuangGai=null,PendingMovementContinuation=null});native.Finish(f);}
    [ProgramContinuation(ProgramContinuationKind.Resume,35400)]private bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouHuangGai:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,35400)]private bool Movement(ProgramSkillFrame f)=>f.MouHuangGai is not null&&f.PendingMovementContinuation is not null&&Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,35400)]private void Invariant(ProgramSkillFrame f){if(f.MouHuangGai is not null&&!Valid(f))throw new InvalidOperationException("Quzhou lost its immutable actual source or entity interval.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,35400)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,35400)]private bool Paid(ProgramSkillFrame f)=>f.MouHuangGai is not null&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangGaiQuzhou)]private PromptChoice Ai(PendingDecision p,ProgramSkillFrame f)=>f.MouHuangGai?.Stage==MouHgStage.Target?
        p.Choices.OrderByDescending(c=>facts.TargetScore(f.OwnerSeat,c.Targets.Single())).First():p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="again")??p.Choices.First();
}
