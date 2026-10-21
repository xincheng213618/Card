using System.Globalization;
namespace CardGame.Core;

internal interface IMouDongZhuoOlFacts : IProgramSelectionQuery
{
    int Turn { get; } int Round { get; } int PlayerCount { get; } int PhaseId { get; } long MovementSequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); string Name(int seat); int Hp(int seat); bool Owns(int seat, string skill);
    SkillProgram Program(string skill); CardLocation Location(int id); IReadOnlyList<MouDongZhuoOlCard> Discardable(int seat);
    IReadOnlyList<MouDongZhuoOlCard> CurrentDiscard(); IEnumerable<T> History<T>() where T : IGameEvent;
    bool ExactStart(ProgramSkillFrame frame, SkillProgramEffectOp operation); bool ParentMatches(ProgramSkillFrame frame);
    int KeepValue(int seat, int cardId); bool Friendly(int owner, int target);
}
internal interface IMouDongZhuoOlNative : IProgramRuntimeOperations
{
    void SetGuanbian(ProgramSkillFrame frame, int amount); void LoseGuanbian(ProgramSkillFrame frame);
    bool Drain(ProgramSkillFrame frame); void Finish(ProgramSkillFrame frame);
    void Discard(ProgramSkillFrame frame, int payer, MouDongZhuoOlCard card); void Give(ProgramSkillFrame frame, int cardId, int recipient);
    void Damage(ProgramSkillFrame frame, int target); bool BeginWine(ProgramSkillFrame frame);
    void MaximumHp(ProgramSkillFrame frame); void Recover(ProgramSkillFrame frame); void Grant(ProgramSkillFrame frame, string skill);
}

[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouDongZhuoOlRuntime(IMouDongZhuoOlFacts facts, IMouDongZhuoOlNative native,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts) : IMouDongZhuoOlRuntime
{
    internal const string Guanbian="mou-ol:guanbian", Xiongni="mou-ol:xiongni", Fengshang="mou-ol:fengshang", Zhibing="mou-ol:zhibing";
    internal const string CostReason="skill-program.mou-ol:xiongni.discard", GiftReason="skill-program.mou-ol:fengshang.give";
    internal static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    private int BlackCount(int owner)=>facts.History<MouDongZhuoOlBlackUseEvent>().Count(e=>e.Actor!=owner&&e.Faction=="qun"&&e.Black);
    private bool Dying(ProgramSkillFrame f)=>f.WindowContext?.Window==SkillProgramTriggerWindow.DyingEntering;
    private IReadOnlyList<MouDongZhuoOlCard> Available(int owner)=>Array.AsReadOnly(facts.CurrentDiscard().Where(c=>c.Suit!=Suit.None&&
        !facts.History<MouDongZhuoOlFengshangEvent>().Any(e=>e.Owner==owner&&e.Round==facts.Round&&e.Suit==c.Suit))
        .GroupBy(c=>c.Suit).Where(g=>g.Count()>=2).SelectMany(g=>g).ToArray());
    internal bool CanFengshang(int owner,bool dying)=>Enumerable.Range(0,facts.PlayerCount).Count(facts.Alive)>=2&&Available(owner).Count>=2&&
        !facts.History<MouDongZhuoOlFengshangEvent>().Any(e=>e.Owner==owner&&e.Turn==facts.Turn&&(dying?e.Dying:!e.Dying&&e.Phase==facts.PhaseId));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,34700)]
    internal bool Trigger(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)=>c.SkillId switch {
        Xiongni=>facts.Discardable(c.OwnerSeat).Count>0,
        Fengshang=>CanFengshang(c.OwnerSeat,true),
        Zhibing=>BlackCount(c.OwnerSeat)>=3,
        Guanbian=>t.Window!=SkillProgramTriggerWindow.RoundEnding||facts.Round==1,
        _=>true };
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition,34700,SkillProgramEffectOp.ResolveMouDongZhuoFengshang)]
    internal bool Activation(CharacterState owner)=>CanFengshang(owner.Seat,false);
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(f.MouDongZhuoOl is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(!facts.ExactStart(f,op))throw new InvalidOperationException("Mou Dong Zhuo lost the original native binding.");
        if(op==SkillProgramEffectOp.InitializeMouDongZhuoGuanbian){native.SetGuanbian(f,facts.PlayerCount);events.AdvanceFact(new MouDongZhuoOlGuanbianEvent(f.Id,f.OwnerSeat,facts.PlayerCount,false));return SkillProgramStepOutcome.Continue;}
        var stage=op switch {SkillProgramEffectOp.RemoveMouDongZhuoGuanbian=>MouDongZhuoOlStage.Finishing,SkillProgramEffectOp.ResolveMouDongZhuoXiongni=>MouDongZhuoOlStage.OwnCost,
            SkillProgramEffectOp.ResolveMouDongZhuoFengshang=>MouDongZhuoOlStage.GiftCard,_=>MouDongZhuoOlStage.ZhibingMaximum};
        var r=new MouDongZhuoOlReceipt(Source(f),f.GameplayHash,op,facts.Turn,facts.Round,frames.ParentOf(f.Id)?.Id??0){Stage=stage,Before=facts.MovementSequence,After=facts.MovementSequence,
            Paid=op is SkillProgramEffectOp.RemoveMouDongZhuoGuanbian or SkillProgramEffectOp.ResolveMouDongZhuoZhibing,
            Participants=Enumerable.Range(1,facts.PlayerCount-1).Select(i=>(i+f.OwnerSeat)%facts.PlayerCount).ToArray(),BlackCount=BlackCount(f.OwnerSeat)};
        frames.ReplaceTop(f=f with{MouDongZhuoOl=r});events.AdvanceFact(new MouDongZhuoOlStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.Turn,r.Round,r.ParentId));
        if(op==SkillProgramEffectOp.RemoveMouDongZhuoGuanbian)RemoveGuanbian(f);
        else if(op==SkillProgramEffectOp.ResolveMouDongZhuoZhibing)events.AdvanceFact(new MouDongZhuoOlZhibingEvent(f.Id,f.OwnerSeat,r.Turn,r.BlackCount));
        Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice Choice(ProgramSkillFrame f,string branch,string text,int card=0,int target=-1)=>new(new($"mou-dong-zhuo.{f.Id}.{branch}.{card}.{target}"),text,card>0?[card]:[],target>=0?[target]:[],
        new Dictionary<string,string>{{"program-action","mou-dong-zhuo-ol"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
    private int Chooser(ProgramSkillFrame f)=>f.MouDongZhuoOl!.Stage==MouDongZhuoOlStage.PeerChoice?f.MouDongZhuoOl.Participants[f.MouDongZhuoOl.Cursor]:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouDongZhuoOl!;var choices=new List<PromptChoice>();
        if(r.Stage is MouDongZhuoOlStage.OwnCost or MouDongZhuoOlStage.PeerChoice){var seat=Chooser(f);foreach(var c in facts.Discardable(seat).Where(c=>r.Stage==MouDongZhuoOlStage.OwnCost||c.Suit==r.Suit))choices.Add(Choice(f,"discard","弃置这张牌",c.Id));
            choices.Add(Choice(f,r.Stage==MouDongZhuoOlStage.OwnCost?"cancel":"damage",r.Stage==MouDongZhuoOlStage.OwnCost?"不发动凶逆":"不弃牌，受到1点伤害"));}
        else if(r.Stage==MouDongZhuoOlStage.GiftCard)foreach(var c in Available(f.OwnerSeat).Where(c=>!r.Gifts.Any(g=>g.CardId==c.Id)&&(r.Gifts.Count==0||c.Suit==r.Suit)))choices.Add(Choice(f,"card","分配弃牌堆中的牌",c.Id));
        else if(r.Stage==MouDongZhuoOlStage.GiftRecipient)foreach(var seat in Enumerable.Range(0,facts.PlayerCount).Where(s=>facts.Alive(s)&&!r.Gifts.Any(g=>g.Recipient==s)))choices.Add(Choice(f,"recipient","交给"+facts.Name(seat),target:seat));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices=Choices(f);if(choices.Count==0){Complete(f);return;}var seat=Chooser(f);frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new(DecisionKind.ProgramTrigger,seat,f.SkillId==Xiongni?"凶逆：选择真实弃牌或承受伤害。":"封赏：分配两张同花色牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=prompts.NextId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,f.SkillId==Xiongni?"凶逆":"封赏","谋董卓","")},facts.Human(seat));
    }
    private static bool Equal(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(p=>b.Parameters.GetValueOrDefault(p.Key)==p.Value);
    [ProgramAnswer("mou-dong-zhuo-ol")]
    internal void Answer(PromptChoice choice)
    {
        if(frames.Top is not ProgramSkillFrame{MouDongZhuoOl:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=Chooser(f)||!Choices(f).Any(c=>Equal(c,choice)))throw new InvalidOperationException("Mou Dong Zhuo's private native choice changed.");
        var payer=Chooser(f);prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        switch(choice.Parameters["branch"]){
            case "cancel":Complete(f);return;
            case "discard":var c=facts.Discardable(payer).Single(c=>c.Id==choice.Cards.Single());frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=r.Stage==MouDongZhuoOlStage.OwnCost?MouDongZhuoOlStage.CostChildren:MouDongZhuoOlStage.PeerChildren,Paid=true,Payer=payer,Suit=r.Stage==MouDongZhuoOlStage.OwnCost?c.Suit:r.Suit,Materials=[c],Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});native.Discard(f,payer,c);Continue(f.Id);return;
            case "damage":frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.Damage,Payer=payer}});events.AdvanceFact(new MouDongZhuoOlDamageIssuedEvent(f.Id,payer,r.Cursor));native.Damage(f,payer);Continue(f.Id);return;
            case "card":var card=Available(f.OwnerSeat).Single(c=>c.Id==choice.Cards.Single());frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.GiftRecipient,SelectedCard=card.Id,Suit=card.Suit}});Publish(f);return;
            case "recipient":var gifts=r.Gifts.Append(new MouDongZhuoOlGift(r.SelectedCard,choice.Targets.Single())).ToArray();frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=gifts.Length==2?MouDongZhuoOlStage.GiftChildren:MouDongZhuoOlStage.GiftCard,Gifts=gifts,SelectedCard=0}});
                if(gifts.Length==2){if(!CanFengshang(f.OwnerSeat,Dying(f))||gifts.Any(g=>facts.Location(g.CardId)!=CardLocation.DiscardPile))throw new InvalidOperationException("Fengshang lost its two actual discard materials before payment.");
                    frames.ReplaceTop(f=f with{MouDongZhuoOl=f.MouDongZhuoOl! with{Paid=true}});events.AdvanceFact(new MouDongZhuoOlFengshangEvent(f.Id,f.OwnerSeat,r.Turn,r.Round,facts.PhaseId,Dying(f),r.Suit));RemoveGuanbian(f);Continue(f.Id);}else Publish(f);return;
            default:throw new InvalidOperationException("Unknown Mou Dong Zhuo choice.");}
    }
    private void RemoveGuanbian(ProgramSkillFrame f)
    {if(f.MouDongZhuoOl!.GuanbianRemoved)return;frames.ReplaceTop(f=f with{MouDongZhuoOl=f.MouDongZhuoOl with{GuanbianRemoved=true}});native.LoseGuanbian(f);}
    private void Continue(long id)
    {
        for(var guard=0;guard<32;guard++){
            if(frames.Top is not ProgramSkillFrame{MouDongZhuoOl:{}r}f||f.Id!=id)return;if(!Valid(f))throw new InvalidOperationException("Mou Dong Zhuo lost his paid frame-owned native continuation.");
            if(r.Stage is MouDongZhuoOlStage.OwnCost or MouDongZhuoOlStage.PeerChoice or MouDongZhuoOlStage.GiftCard or MouDongZhuoOlStage.GiftRecipient){Publish(f);return;}
            if(r.Stage==MouDongZhuoOlStage.Wine)return;
            if(f.AttackAttempt is not null||native.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouDongZhuoOl!;
            frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
            if(facts.Finished){Complete(f);return;}
            if(r.Stage==MouDongZhuoOlStage.CostChildren){RemoveGuanbian(f);f=frames.ActiveProgram(id);r=f.MouDongZhuoOl!;if(native.Drain(f))return;frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.PeerChildren,Cursor=-1}});continue;}
            if(r.Stage is MouDongZhuoOlStage.PeerChildren or MouDongZhuoOlStage.Damage){var next=r.Cursor+1;while(next<r.Participants.Count&&!facts.Alive(r.Participants[next]))next++;if(next>=r.Participants.Count){Complete(f);return;}frames.ReplaceTop(f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.PeerChoice,Cursor=next,Materials=[]}});continue;}
            if(r.Stage==MouDongZhuoOlStage.GiftChildren){if(r.Cursor<r.Gifts.Count){var g=r.Gifts[r.Cursor];frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Cursor=r.Cursor+1,Before=facts.MovementSequence,After=facts.MovementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});if(facts.Alive(g.Recipient)&&facts.Location(g.CardId)==CardLocation.DiscardPile)native.Give(f,g.CardId,g.Recipient);continue;}
                if(facts.History<MouDongZhuoOlGiftEvent>().Any(g=>g.FrameId==f.Id&&g.Recipient==f.OwnerSeat)||!facts.Alive(f.OwnerSeat)){Complete(f);return;}frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.Wine}});if(!native.BeginWine(f)){frames.ReplaceTop(f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.Finishing}});continue;}return;}
            if(r.Stage==MouDongZhuoOlStage.WineChildren){if(!r.WineReturned)throw new InvalidOperationException("Fengshang Wine has not really returned.");Complete(f);return;}
            if(r.Stage==MouDongZhuoOlStage.ZhibingMaximum){if(r.BlackCount<3||!facts.Alive(f.OwnerSeat)){Complete(f);return;}frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.ZhibingRecovery}});native.MaximumHp(f);continue;}
            if(r.Stage==MouDongZhuoOlStage.ZhibingRecovery){frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.ZhibingGrants}});if(facts.Alive(f.OwnerSeat))native.Recover(f);continue;}
            if(r.Stage==MouDongZhuoOlStage.ZhibingGrants){frames.ReplaceTop(f=f with{MouDongZhuoOl=r with{Stage=MouDongZhuoOlStage.Finishing}});if(facts.Alive(f.OwnerSeat)){if(r.BlackCount>=6)native.Grant(f,"classic:fencheng");if(r.BlackCount>=9)native.Grant(f,"classic:benghuai");}continue;}
            Complete(f);return;
        }throw new InvalidOperationException("Mou Dong Zhuo exceeded his bounded native cursor.");
    }
    internal bool Valid(ProgramSkillFrame f)=>f.InstructionIndex==1&&f.MouDongZhuoOl is{}r&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId).GameplayHash==f.GameplayHash&&r.Turn==facts.Turn&&r.Round==facts.Round&&r.ParentId==(frames.ParentOf(f.Id)?.Id??0)&&Enum.IsDefined(r.Stage)&&r.After>=r.Before&&
        facts.History<MouDongZhuoOlStartedEvent>().Count(e=>e==new MouDongZhuoOlStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.Turn,r.Round,r.ParentId))==1&&facts.ParentMatches(f);
    private void Complete(ProgramSkillFrame f){events.AdvanceFact(new MouDongZhuoOlCompletedEvent(f.Id));frames.ReplaceTop(f=f with{MouDongZhuoOl=null,PendingMovementContinuation=null});native.Finish(f);}
    [ProgramContinuation(ProgramContinuationKind.Resume,34700)]internal bool Resume(long id){if(frames.Top is not ProgramSkillFrame{MouDongZhuoOl:not null}f||f.Id!=id)return false;Continue(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,34700)]internal bool Movement(ProgramSkillFrame f)=>f.MouDongZhuoOl is not null&&Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,34700)]internal void Invariant(ProgramSkillFrame f){if(f.MouDongZhuoOl is not null&&!Valid(f))throw new InvalidOperationException("Invalid Mou Dong Zhuo owning receipt.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34700)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34700)]internal bool Paid(ProgramSkillFrame f)=>f.MouDongZhuoOl is{Paid:true}&&Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDongZhuoXiongni,SkillProgramEffectOp.ResolveMouDongZhuoFengshang)]
    internal PromptChoice Ai(PendingDecision d,ProgramSkillFrame f)
    {var r=f.MouDongZhuoOl!;if(r.Stage==MouDongZhuoOlStage.GiftRecipient)return d.Choices.OrderByDescending(c=>facts.Friendly(f.OwnerSeat,c.Targets.Single())?1:0).First();
        return d.Choices.Where(c=>c.Cards.Count>0).OrderBy(c=>facts.KeepValue(d.PlayerSeat,c.Cards.Single())).FirstOrDefault()??d.Choices.Last();}
}
