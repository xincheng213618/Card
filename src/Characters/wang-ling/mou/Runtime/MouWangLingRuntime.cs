using System.Globalization;
namespace CardGame.Core;

internal interface IMouWangLingFacts : IProgramSelectionQuery
{
    int Turn { get; }
    int CurrentSeat { get; }
    int PlayerCount { get; }
    long MovementSequence { get; }
    bool GameOver { get; }
    bool Alive(int seat);
    bool Human(int seat);
    int MaximumHp(int seat);
    string Name(int seat);
    SkillProgram Program(string skill);
    CardLocation Location(int id);
    IReadOnlyList<Card> Hand(int seat);
    CardKind HandKind(int seat, int id);
    IEnumerable<T> History<T>() where T : IGameEvent;
    IReadOnlyList<CardMovementRecord> Movements(long before, long after);
    double KeepValue(int owner, int id);
    double TargetValue(int owner, int target);
    bool ParentMatches(ProgramSkillFrame frame);
}
internal interface IMouWangLingOperations : IProgramRuntimeOperations
{
    void Give(ProgramSkillFrame frame, int cardId, int target);
    void Discard(ProgramSkillFrame frame, IReadOnlyList<int> ids);
    bool Drain(ProgramSkillFrame frame);
    void Finish(ProgramSkillFrame frame);
}

[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MouWangLingRuntime(IMouWangLingFacts facts, IMouWangLingOperations operations,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts,
    IProgramOperationDraws draws, IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations, IProgramAwakeningOverrideQuery awakeningConditions) : IMouWangLingRuntime
{
    internal const string GiftReason="skill-program.mou:jichou.give", JichouDrawReason="skill-program.mou:jichou.draw",
        ZifuDrawReason="skill-program.mou:zifu.draw", DiscardReason="skill-program.mou:zifu.discard";
    internal static CardKind CardName(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
    private static CardConversionSource Source(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    internal int GivenNames(int owner)=>facts.History<MouWangLingGiftEvent>().Where(e=>e.OwnerSeat==owner).Select(e=>e.Name).Distinct().Count();
    private CurrentTurnCardUseKindsRecordedEvent[] TurnUses(int owner)=>facts.History<CurrentTurnCardUseKindsRecordedEvent>().Where(e=>
        e.TurnNumber==facts.Turn&&e.TurnOwnerSeat==owner&&e.ActorSeat==owner&&e.WholeActualTurnUseSequence is>0).ToArray();
    private bool UniqueTurn(int owner)
    {
        var uses=TurnUses(owner);
        return owner==facts.CurrentSeat&&uses.Length>0&&uses.All(e=>e.EffectiveCardKind is not null)&&
            uses.Select(e=>CardName(e.EffectiveCardKind!.Value)).Distinct().Count()==uses.Length;
    }
    private IReadOnlyList<MouWangLingMaterial> GiftMaterials(int owner)
    {
        if(!UniqueTurn(owner))return [];
        var entered=facts.History<NativeTrueUseProcessingEntryEvent>().ToArray();
        return Array.AsReadOnly(TurnUses(owner).SelectMany(use=>entered.Where(e=>e.ParentFrameId==use.OriginFrameId&&
            (use.RoundActionType==CardActionType.Use?e.Kind==NativeTrueUseDiscardKind.Use&&e.ParentActionId==use.CardActionId:
                e.ProviderSeat==owner&&e.Kind==(use.IsNullificationUse?NativeTrueUseDiscardKind.Nullification:NativeTrueUseDiscardKind.Dodge))))
            .DistinctBy(e=>e.CardId).Where(e=>facts.Location(e.CardId)==CardLocation.DiscardPile)
            .Select(e=>new MouWangLingMaterial(e.CardId,CardName(e.CardKind))).ToArray());
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,34500)]
    internal bool Qualifies(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>c.SkillId switch {
        MouWangLingComposition.Jichou=>c.OwnerSeat==facts.CurrentSeat&&GiftMaterials(c.OwnerSeat).Count>0,
        MouWangLingComposition.Mouli=>(GivenNames(c.OwnerSeat)>=5||awakeningConditions.Granted(c.OwnerSeat,c.SkillId,c.BindingId))&&!facts.History<MouWangLingAwakeningAcceptedEvent>().Any(e=>e.OwnerSeat==c.OwnerSeat),
        _=>true };
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied,SkillProgramEffectOp operation)
    {
        var f=frames.ActiveProgram(supplied.Id);
        if(f.MouWangLing is not null){Continue(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.InstructionIndex!=1||!facts.ParentMatches(f))throw new InvalidOperationException("Wang Ling lost his actual native lifecycle entry.");
        var zifu=operation==SkillProgramEffectOp.ResolveMouWangLingZifu;
        var awakening=operation==SkillProgramEffectOp.PrepareMouWangLingMouli;
        if(awakening&&GivenNames(f.OwnerSeat)<5&&!awakeningConditions.Granted(f.OwnerSeat,f.SkillId,f.TriggerId)||!awakening&&!zifu&&!UniqueTurn(f.OwnerSeat))throw new InvalidOperationException("Wang Ling lost his actual name qualification.");
        var r=new MouWangLingReceipt(Source(f),f.GameplayHash,operation,facts.Turn,frames.ParentOf(f.Id)!.Id){
            Stage=awakening?MouWangLingStage.Awakening:zifu?MouWangLingStage.Drawing:MouWangLingStage.GiftCards,
            Paid=awakening||zifu,Before=facts.MovementSequence,After=facts.MovementSequence,
            Materials=zifu||awakening?[]:GiftMaterials(f.OwnerSeat)};
        frames.ReplaceTop(f=f with{MouWangLing=r});events.AdvanceFact(new MouWangLingStartedEvent(f.Id,r.Source,r.GameplayHash,operation,r.ActualTurn,r.ParentFrameId));
        foreach(var item in r.Materials.Select((m,i)=>(m,i)))events.AdvanceFact(new MouWangLingMaterialEvent(f.Id,item.i,item.m.CardId,item.m.Name));
        if(awakening){events.AdvanceFact(new MouWangLingAwakeningAcceptedEvent(f.Id,f.OwnerSeat,GivenNames(f.OwnerSeat)));return SkillProgramStepOutcome.Continue;}
        if(zifu)BeginDraw(f,Math.Max(0,Math.Min(5,Math.Max(0,facts.MaximumHp(f.OwnerSeat)))-facts.Hand(f.OwnerSeat).Count));else Publish(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice Choice(ProgramSkillFrame f,string branch,string text,int card=0,int target=-1)=>new(
        new($"mou-wang-ling.{f.Id}.{branch}.{card}.{target}"),text,card>0?[card]:[],target>=0?[target]:[],
        new Dictionary<string,string>{{"program-action","mou-wang-ling"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}});
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r=f.MouWangLing!;var choices=new List<PromptChoice>();
        if(r.Stage==MouWangLingStage.GiftCards)
        {
            if(r.Gifts.Count<facts.PlayerCount)foreach(var m in r.Materials.Where(m=>facts.Location(m.CardId)==CardLocation.DiscardPile&&!r.Gifts.Any(g=>g.CardId==m.CardId)))
                choices.Add(Choice(f,"card","分配弃牌堆中的【"+CardCatalog.Get(m.Name).DisplayName+"】",m.CardId));
            choices.Add(Choice(f,"confirm",$"确认分配{r.Gifts.Count}张牌"));
        }
        else if(r.Stage==MouWangLingStage.GiftRecipient)
        {
            foreach(var seat in Enumerable.Range(0,facts.PlayerCount).Where(s=>facts.Alive(s)&&!r.Gifts.Any(g=>g.RecipientSeat==s)))choices.Add(Choice(f,"recipient","交给"+facts.Name(seat),target:seat));
            choices.Add(Choice(f,"back","重新选择牌"));
        }
        else if(r.Stage==MouWangLingStage.Retaining)
        {
            var group=r.Materials.GroupBy(m=>m.Name).FirstOrDefault(g=>!g.Any(m=>r.Kept.Contains(m.CardId)));
            if(group is not null)foreach(var m in group.Where(m=>facts.Location(m.CardId)==CardLocation.Hand(f.OwnerSeat)))choices.Add(Choice(f,"keep","保留这一张【"+CardCatalog.Get(m.Name).DisplayName+"】",m.CardId));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void Publish(ProgramSkillFrame f)
    {
        if(!facts.Alive(f.OwnerSeat)||facts.GameOver){Finish(f);return;}
        var choices=Choices(f);if(choices.Count==0){Finish(f);return;}
        var skill=f.SkillId;var name=skill==MouWangLingComposition.Jichou?"集筹":"自缚";
        frames.ReplaceTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        prompts.Set(new(DecisionKind.ProgramTrigger,f.OwnerSeat,name+"：请选择实际的牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){
            PromptId=prompts.NextId(),IsPrivate=true,Choices=choices,SkillPrompt=new(skill,name,name,name)},facts.Human(f.OwnerSeat));
    }
    private static bool Equal(PromptChoice a,PromptChoice b)=>a.Id==b.Id&&a.Cards.SequenceEqual(b.Cards)&&a.Targets.SequenceEqual(b.Targets)&&a.Parameters.Count==b.Parameters.Count&&a.Parameters.All(p=>b.Parameters.GetValueOrDefault(p.Key)==p.Value);
    [ProgramAnswer("mou-wang-ling")]
    internal void Answer(PromptChoice choice)
    {
        if(frames.Top is not ProgramSkillFrame{MouWangLing:{}r}f||!Valid(f)||prompts.Current?.PlayerSeat!=f.OwnerSeat||!Choices(f).Any(c=>Equal(c,choice)))throw new InvalidOperationException("Wang Ling requires his actual privately published choice.");
        prompts.Clear();frames.ReplaceTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        switch(choice.Parameters["branch"])
        {
            case "card":frames.ReplaceTop(f=f with{MouWangLing=r with{SelectedCard=choice.Cards.Single(),Stage=MouWangLingStage.GiftRecipient}});Publish(f);break;
            case "back":frames.ReplaceTop(f=f with{MouWangLing=r with{SelectedCard=0,Stage=MouWangLingStage.GiftCards}});Publish(f);break;
            case "recipient":
                var material=r.Materials.Single(m=>m.CardId==r.SelectedCard);
                frames.ReplaceTop(f=f with{MouWangLing=r with{SelectedCard=0,Stage=MouWangLingStage.GiftCards,Gifts=r.Gifts.Append(new(material.CardId,material.Name,choice.Targets.Single())).ToArray()}});Publish(f);break;
            case "confirm":
                frames.ReplaceTop(f=f with{MouWangLing=r with{Paid=true,Stage=MouWangLingStage.GiftChildren}});
                events.AdvanceFact(new MouWangLingGiftAcceptedEvent(f.Id,r.Gifts.Count));Continue(f.Id);break;
            case "keep":
                var kept=r.Materials.Single(m=>m.CardId==choice.Cards.Single());
                frames.ReplaceTop(f=f with{MouWangLing=r=r with{Kept=r.Kept.Append(kept.CardId).ToArray()}});
                events.AdvanceFact(new MouWangLingKeptEvent(f.Id,kept.CardId,kept.Name));ContinueRetention(f);break;
            default:throw new InvalidOperationException("Unknown Wang Ling choice.");
        }
    }
    private void BeginDraw(ProgramSkillFrame f,int count)
    {
        if(count<=0||!facts.Alive(f.OwnerSeat)||facts.GameOver){Finish(f);return;}
        var r=f.MouWangLing!;frames.ReplaceTop(f=f with{MouWangLing=r with{Stage=MouWangLingStage.Drawing,Requested=count,Draw=null},PendingMovementContinuation=null});
        draws.BeginOperationProgramDraw(f,f.OwnerSeat,count,new(r.Operation==SkillProgramEffectOp.ResolveMouWangLingJichou?JichouDrawReason:ZifuDrawReason));
    }
    private void Continue(long id)
    {
        if(frames.Top is not ProgramSkillFrame{MouWangLing:{}r}f||f.Id!=id)return;
        if(!Valid(f))throw new InvalidOperationException("Wang Ling lost his original frame-owned continuation.");
        if(r.Stage==MouWangLingStage.Awakening)return;
        if(r.Stage is MouWangLingStage.GiftChildren or MouWangLingStage.DrawChildren or MouWangLingStage.DiscardChildren)
        {
            if(operations.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouWangLing!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
            if(facts.GameOver){Finish(f);return;}
            if(r.Stage==MouWangLingStage.GiftChildren)
            {
                while(r.Cursor<r.Gifts.Count)
                {
                    var index=r.Cursor;var gift=r.Gifts[index];var before=facts.MovementSequence;
                    frames.ReplaceTop(f=f with{MouWangLing=r=r with{Cursor=index+1,Before=before,After=before}});
                    if(!facts.Alive(gift.RecipientSeat)||facts.Location(gift.CardId)!=CardLocation.DiscardPile)continue;
                    var first=!facts.History<MouWangLingGiftEvent>().Any(e=>e.OwnerSeat==f.OwnerSeat&&e.Name==gift.Name);
                    operations.Give(f,gift.CardId,gift.RecipientSeat);var after=facts.MovementSequence;
                    var moved=facts.Movements(before,after).SingleOrDefault(m=>m.CardId==gift.CardId&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.Hand(gift.RecipientSeat)&&m.Reason.Value==GiftReason);
                    frames.ReplaceTop(f=f with{MouWangLing=r=r with{After=after,FirstNames=r.FirstNames+(moved is not null&&first?1:0)},PendingMovementContinuation=new(gift.RecipientSeat,0,null)});
                    if(moved is not null)events.AdvanceFact(new MouWangLingGiftEvent(f.Id,f.OwnerSeat,index,gift.CardId,gift.Name,gift.RecipientSeat,moved.Sequence,first));
                    if(operations.Drain(f))return;f=frames.ActiveProgram(id);r=f.MouWangLing!;frames.ReplaceTop(f=f with{PendingMovementContinuation=null});
                    if(facts.GameOver){Finish(f);return;}
                }
                BeginDraw(f,r.FirstNames);return;
            }
            if(r.Stage==MouWangLingStage.DrawChildren&&r.Operation==SkillProgramEffectOp.ResolveMouWangLingZifu&&r.Draw?.ActualDrawIds.Count>0&&facts.Alive(f.OwnerSeat))
            {
                var materials=facts.Hand(f.OwnerSeat).Select(c=>new MouWangLingMaterial(c.Id,CardName(facts.HandKind(f.OwnerSeat,c.Id)))).ToArray();
                var automatic=materials.GroupBy(m=>m.Name).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray();
                frames.ReplaceTop(f=f with{MouWangLing=r with{Stage=MouWangLingStage.Retaining,Materials=materials,Kept=automatic.Select(m=>m.CardId).ToArray()}});
                foreach(var m in automatic)events.AdvanceFact(new MouWangLingKeptEvent(f.Id,m.CardId,m.Name));ContinueRetention(f);return;
            }
            Finish(f);return;
        }
        if(r.Stage==MouWangLingStage.Retaining){ContinueRetention(f);return;}
        if(r.Stage is MouWangLingStage.GiftCards or MouWangLingStage.GiftRecipient)Publish(f);
    }
    private void ContinueRetention(ProgramSkillFrame f)
    {
        var r=f.MouWangLing!;
        if(r.Materials.GroupBy(m=>m.Name).Any(g=>!g.Any(m=>r.Kept.Contains(m.CardId)))){Publish(f);return;}
        var ids=r.Materials.Where(m=>!r.Kept.Contains(m.CardId)&&facts.Location(m.CardId)==CardLocation.Hand(f.OwnerSeat)).Select(m=>m.CardId).ToArray();
        if(ids.Length==0){Finish(f);return;}
        var before=facts.MovementSequence;frames.ReplaceTop(f=f with{MouWangLing=r with{Stage=MouWangLingStage.DiscardChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        operations.Discard(f,ids);var after=facts.MovementSequence;
        frames.ReplaceTop(f=f with{MouWangLing=f.MouWangLing!with{After=after}});
        foreach(var m in facts.Movements(before,after).Where(m=>ids.Contains(m.CardId)&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==DiscardReason))events.AdvanceFact(new MouWangLingDiscardedEvent(f.Id,m.CardId,r.Materials.Single(x=>x.CardId==m.CardId).Name,m.To,m.Sequence));
        Continue(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouWangLingJichou,SkillProgramEffectOp.ResolveMouWangLingZifu)]
    internal void DrawReturned(ProgramSkillFrame f,NativeDrawOutcome outcome)
    {
        if(f.MouWangLing is not{Stage:MouWangLingStage.Drawing,Draw:null}r||outcome.RequestedCount!=r.Requested||outcome.RecipientSeat!=f.OwnerSeat)throw new InvalidOperationException("Wang Ling cannot replay or replace a paid native draw.");
        frames.ReplaceTop(f=f with{MouWangLing=r with{Stage=MouWangLingStage.DrawChildren,Draw=outcome,Before=outcome.SequenceBefore,After=outcome.SequenceAfter},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        events.AdvanceFact(new MouWangLingDrawReturnedEvent(f.Id,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.ReplacementGainedIds.Count,outcome.AttemptFrameId));Continue(f.Id);
    }
    private void Finish(ProgramSkillFrame f)
    {
        var r=f.MouWangLing!;events.AdvanceFact(new MouWangLingCompletedEvent(f.Id,r.Operation,r.FirstNames));
        frames.ReplaceTop(f=f with{MouWangLing=null,PendingMovementContinuation=null});operations.Finish(f);
    }
    private bool Valid(ProgramSkillFrame f)=>f.MouWangLing is{}r&&r.Source==Source(f)&&r.GameplayHash==f.GameplayHash&&facts.Program(f.SkillId).GameplayHash==f.GameplayHash&&
        r.ParentFrameId==frames.ParentOf(f.Id)?.Id&&(r.Stage==MouWangLingStage.Awakening?f.InstructionIndex is>=1 and<=4:f.InstructionIndex==1)&&
        r.Gifts.Select(g=>g.CardId).Distinct().Count()==r.Gifts.Count&&r.Gifts.Select(g=>g.RecipientSeat).Distinct().Count()==r.Gifts.Count&&
        r.Kept.Distinct().Count()==r.Kept.Count&&r.Kept.All(id=>r.Materials.Any(m=>m.CardId==id))&&r.Before<=r.After&&r.After<=facts.MovementSequence&&
        facts.History<MouWangLingStartedEvent>().Count(e=>e==new MouWangLingStartedEvent(f.Id,r.Source,r.GameplayHash,r.Operation,r.ActualTurn,r.ParentFrameId))==1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,34500)]
    internal void Assert(ProgramSkillFrame f){if(f.MouWangLing is not null&&!Valid(f))throw new InvalidOperationException("Wang Ling lost his original accepted source or physical payment cursor.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,34500)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,34500)]
    internal bool PreservePaid(ProgramSkillFrame f)=>f.MouWangLing is{Paid:true}&&Valid(f);
    [ProgramContinuation(ProgramContinuationKind.Resume,34500)]
    internal bool Resume(long id)
    {if(frames.Top is not ProgramSkillFrame{MouWangLing:{}r}f||f.Id!=id||r.Stage==MouWangLingStage.Awakening)return false;Continue(id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouWangLingJichou,SkillProgramEffectOp.ResolveMouWangLingZifu)]
    internal PromptChoice Ai(PendingDecision p,ProgramSkillFrame f,SkillProgramEffect effect)=>f.MouWangLing!.Stage switch {
        MouWangLingStage.GiftCards=>p.Choices.FirstOrDefault(c=>c.Cards.Count==1)??p.Choices.Single(),
        MouWangLingStage.GiftRecipient=>p.Choices.Where(c=>c.Targets.Count==1).OrderByDescending(c=>facts.TargetValue(f.OwnerSeat,c.Targets[0])).First(),
        _=>p.Choices.OrderByDescending(c=>c.Cards.Count==1?facts.KeepValue(f.OwnerSeat,c.Cards[0]):0).First() };
}
