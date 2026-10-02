namespace CardGame.Core;

public sealed record ProgramSelectedParticipantDiscardDraft(int TargetSeat, int Cursor);
public sealed record ProgramDamageCardOffer(int SourceSeat, string SourceBind, bool Refusing = false,
    int RequiredDiscardCount = 0, IReadOnlyList<int>? SelectedDiscardIds = null);
public sealed record ProgramPrivateCardOfferResolvedEvent(int OwnerSeat, int SourceSeat, int OfferedCount, bool Accepted, bool Prevented) : IGameEvent;
public interface IPrivateOfferProgramEffectHost
{
    SkillProgramStepOutcome DiscardSelectedParticipantCards(ProgramSkillFrame frame);
    SkillProgramStepOutcome OfferBoundCardsForDamagePrevention(ProgramSkillFrame frame,string bind);
}
internal sealed class DiscardSelectedParticipantCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardSelectedParticipantCards;
    public override ISkillProgramEffectHandler Handler { get; }=new DiscardSelectedParticipantCardsHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; }=new(ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.SelectedTargets) throw new InvalidOperationException("Participant discard requires selectedTargets.");
        var effect=new SkillProgramEffect(Op,SkillProgramEffectTarget.SelectedTargets,0,r.Condition());RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new ReadTargetSet(1)];
}
public sealed class DiscardSelectedParticipantCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardSelectedParticipantCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)=>((IPrivateOfferProgramEffectHost)host).DiscardSelectedParticipantCards(frame);
}
internal sealed class OfferBoundCardsForDamagePreventionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferBoundCardsForDamagePrevention;
    public override ISkillProgramEffectHandler Handler { get; }=new OfferBoundCardsForDamagePreventionHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; }=new(ProgramOperationAiSemantic.PreventCurrentDamage,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","condition");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind"));RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new RequireOwnedCardSet(effect.SourceBind!,SkillProgramEffectTarget.Owner,null,[CardZoneKind.Hand,CardZoneKind.Equipment]),new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
public sealed class OfferBoundCardsForDamagePreventionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferBoundCardsForDamagePrevention;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)=>((IPrivateOfferProgramEffectHost)host).OfferBoundCardsForDamagePrevention(frame,effect.SourceBind!);
}
public sealed partial class GameEngine
{
    private int CountSelectedTargetsHandGreaterThanLord(ProgramSkillFrame frame)
    {
        var lord=_players.SingleOrDefault(p=>p.IsAlive && p.Role==Role.Lord);
        return lord is null ? 0 : frame.SelectedTargetSeats.Count(seat=>_players[seat].IsAlive && GetHand(_players[seat]).Count>GetHand(lord).Count);
    }
    private SkillProgramStepOutcome DiscardProgramSelectedParticipantCards(ProgramSkillFrame frame)
    {
        var active=GetActiveProgramFrame(frame.Id);
        var key="participant-discard-cursor-"+(active.InstructionIndex-1);
        var cursor=active.NumberBindings.SingleOrDefault(b=>b.Name==key)?.Value??0;
        if(cursor>=active.SelectedTargetSeats.Count) return SkillProgramStepOutcome.Continue;
        var seat=active.SelectedTargetSeats[cursor];
        active=active with { NumberBindings=active.NumberBindings.Where(b=>b.Name!=key).Append(new ProgramSkillNumberBinding(key,cursor+1)).ToArray(),ReexecuteParticipantInstruction=true };
        ReplaceRuntimeTop(active);
        if(!_players[seat].IsAlive || !HasDiscardableHeBy(frame.OwnerSeat,seat)&&GetJudgment(_players[seat]).Count==0) return SkillProgramStepOutcome.Continue;
        active=active with { SelectedParticipantDiscard=new(seat,cursor),ReexecuteParticipantInstruction=false };
        ReplaceRuntimeTop(active);PublishSelectedParticipantDiscard(active);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> SelectedParticipantDiscardChoices(ProgramSkillFrame frame)
    {
        var draft=frame.SelectedParticipantDiscard!;var choices=new List<PromptChoice>();
        foreach(var zone in new[]{CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.Judgment})
        {
            var cards=_cardZones.CardsAt(new CardLocation(zone,draft.TargetSeat));
            for(var index=0;index<cards.Count;index++)
            {
                var card=cards[index];var opaque=zone==CardZoneKind.Hand && draft.TargetSeat!=frame.OwnerSeat;
                if(IsForeignEquipmentDiscardPrevented(frame.OwnerSeat,card,new CardLocation(zone,draft.TargetSeat),OwnedCardMoveIntent.Discard))continue;
                choices.Add(new(new ChoiceId($"participant-discard.{frame.Id}.{draft.Cursor}.{zone}.{index}"),opaque ? $"弃置 {_players[draft.TargetSeat].Name} 的第{index+1}张手牌" : $"弃置【{card.DisplayName}】",opaque ? []:[card.Id],[draft.TargetSeat],new Dictionary<string,string>{["program-action"]="participant-discard",["zone"]=zone.ToString(),["slot"]=index.ToString()}));
            }
        }
        return choices;
    }
    private void PublishSelectedParticipantDiscard(ProgramSkillFrame frame)
    {
        var choices=SelectedParticipantDiscardChoices(frame);var skill=_contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,frame.OwnerSeat,$"{skill.Name}：弃置 {_players[frame.SelectedParticipantDiscard!.TargetSeat].Name} 一张牌",choices.SelectMany(c=>c.Cards).ToArray(),[frame.SelectedParticipantDiscard.TargetSeat],frame.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void ResolveSelectedParticipantDiscard(PromptChoice choice)
    {
        var frame=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Missing participant discard frame.");
        var draft=frame.SelectedParticipantDiscard??throw new InvalidOperationException("Missing participant discard draft.");
        var paused=ProgramInstructionResolver.Default.Resolve(frame,_contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if(paused.Op!=SkillProgramEffectOp.DiscardSelectedParticipantCards || _pendingDecision?.PlayerSeat!=frame.OwnerSeat || !AssistedChoicesEqual([choice],[SelectedParticipantDiscardChoices(frame).Single(c=>c.Id==choice.Id)])) throw new InvalidOperationException("Participant discard choice changed.");
        var zone=Enum.Parse<CardZoneKind>(choice.Parameters["zone"]);var slot=int.Parse(choice.Parameters["slot"]);var from=new CardLocation(zone,draft.TargetSeat);var card=_cardZones.CardsAt(from)[slot];
        if(IsForeignEquipmentDiscardPrevented(frame.OwnerSeat,card,from,OwnedCardMoveIntent.Discard))throw new InvalidOperationException("Participant discard is no longer available.");
        ClearPendingDecision();ReplaceRuntimeTop(frame with {SelectedParticipantDiscard=null,ReexecuteParticipantInstruction=true});
        MoveCard(card,from,CardLocation.DiscardPile,new CardMoveReason($"skill-program.{frame.SkillId}.ChooseOtherOwnedCardDiscard"));
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
    private static string DamageOfferPairKey(int source)=>"successful-damage-offer:source:"+source;
    private bool IsDamageOfferPairBlocked(int owner,string skill,int source)=>_skillRuntimeState.GetUsage(owner,skill,DamageOfferPairKey(source),SkillUsageScope.Game)>0;
    private SkillProgramStepOutcome OfferProgramBoundCardsForDamagePrevention(ProgramSkillFrame frame,string bind)
    {
        var active=GetActiveProgramFrame(frame.Id);
        if(active.WindowContext is not {Window:SkillProgramTriggerWindow.BeforeDamageApplied,SourceSeat:{ } source,TargetSeat:{ } target} || target!=active.OwnerSeat || source==target || !_players[source].IsAlive || IsDamageOfferPairBlocked(target,active.SkillId,source)) throw new InvalidOperationException("A private damage offer requires an unused other source pair.");
        var set=GetProgramCardSet(active,bind);
        if(set.CardIds.Select((id,i)=>(id,i)).Any(c=>set.SourceLocations[c.i].OwnerSeat!=target || set.SourceLocations[c.i].Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || _cardZones.GetLocation(c.id)!=set.SourceLocations[c.i])) throw new InvalidOperationException("Private offered cards must remain in the actual owner zones.");
        active=active with {DamageCardOffer=new(source,bind)};ReplaceRuntimeTop(active);PublishDamageCardOffer(active);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DamageCardOfferChoices(ProgramSkillFrame frame)
    {
        var draft=frame.DamageCardOffer!;var set=GetProgramCardSet(frame,draft.SourceBind);var choices=new List<PromptChoice>();
        if(!draft.Refusing)
        {
            foreach(var id in set.CardIds)
            {
                var location=_cardZones.GetLocation(id);var card=_cardZones.CardsAt(location).Single(c=>c.Id==id);
                choices.Add(new(new ChoiceId($"private-offer.{frame.Id}.gain.{id}"),$"获得【{card.DisplayName}】并防止此伤害",[id],[],new Dictionary<string,string>{["program-action"]="damage-card-offer",["option"]="gain"}));
            }
            choices.Add(new(new ChoiceId($"private-offer.{frame.Id}.refuse"),$"弃置{set.CardIds.Count}张牌，继续造成伤害",[],[],new Dictionary<string,string>{["program-action"]="damage-card-offer",["option"]="refuse"}));
        }
        else
            foreach(var card in GetHand(_players[draft.SourceSeat]).Concat(GetEquipment(_players[draft.SourceSeat])).Where(c=>draft.SelectedDiscardIds?.Contains(c.Id)!=true))
                choices.Add(new(new ChoiceId($"private-offer.{frame.Id}.discard.{card.Id}"),$"弃置【{card.DisplayName}】",[card.Id],[],new Dictionary<string,string>{["program-action"]="damage-card-offer",["option"]="discard"}));
        return choices;
    }
    private void PublishDamageCardOffer(ProgramSkillFrame frame)
    {
        var draft=frame.DamageCardOffer!;var skill=_contentRegistry.GetSkill(frame.SkillId);var choices=DamageCardOfferChoices(frame);
        _pendingDecision=new(DecisionKind.ProgramTrigger,draft.SourceSeat,draft.Refusing ? $"{skill.Name}：选择弃牌费用" : $"{skill.Name}：观看这些牌并选择",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],frame.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(frame.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[draft.SourceSeat].IsHuman ? EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    private void ResolveDamageCardOffer(PromptChoice choice)
    {
        var frame=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Missing private offer frame.");var draft=frame.DamageCardOffer??throw new InvalidOperationException("Missing private offer draft.");
        var paused=ProgramInstructionResolver.Default.Resolve(frame,_contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if(paused.Op!=SkillProgramEffectOp.OfferBoundCardsForDamagePrevention || paused.SourceBind!=draft.SourceBind || _pendingDecision?.PlayerSeat!=draft.SourceSeat || !AssistedChoicesEqual([choice],[DamageCardOfferChoices(frame).Single(c=>c.Id==choice.Id)])) throw new InvalidOperationException("Private offer answer changed.");
        var set=GetProgramCardSet(frame,draft.SourceBind);var option=choice.Parameters["option"];
        if(option=="gain")
        {
            if(frame.WindowContext?.ParentFrameId is not { } parent || _resolutionStack.Count<2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame before || before.Id!=parent || before.Prevented) throw new InvalidOperationException("Private offer no longer has preventable damage.");
            var id=choice.Cards.Single();var index=set.CardIds.ToList().IndexOf(id);var from=set.SourceLocations[index];var card=_cardZones.CardsAt(from).Single(c=>c.Id==id);
            ClearPendingDecision();ReplaceRuntimeTop(frame with {DamageCardOffer=null});
            MoveCard(card,from,CardLocation.Hand(draft.SourceSeat),new CardMoveReason($"skill-program.{frame.SkillId}.GiveSelected"));
            if(_cardZones.GetLocation(id)!=CardLocation.Hand(draft.SourceSeat)) throw new InvalidOperationException("Private offer gain was not committed.");
            PreventProgramCurrentDamage(GetActiveProgramFrame(frame.Id));
            if(!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat,frame.SkillId,DamageOfferPairKey(draft.SourceSeat),SkillUsageScope.Game,1)) throw new InvalidOperationException("Successful damage source pair was already consumed.");
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(frame.OwnerSeat,frame.SkillId,DamageOfferPairKey(draft.SourceSeat),SkillUsageScope.Game,1));
            AdvanceEventRulesAndQueueFact(new ProgramPrivateCardOfferResolvedEvent(frame.OwnerSeat,draft.SourceSeat,set.CardIds.Count,true,true));
            if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if(option=="refuse")
        {
            var count=Math.Min(set.CardIds.Count,GetHand(_players[draft.SourceSeat]).Count+GetEquipment(_players[draft.SourceSeat]).Count);
            ClearPendingDecision();
            if(count==0){ReplaceRuntimeTop(frame with {DamageCardOffer=null});AdvanceEventRulesAndQueueFact(new ProgramPrivateCardOfferResolvedEvent(frame.OwnerSeat,draft.SourceSeat,set.CardIds.Count,false,false));AdvanceRuntimeProgram(frame.Id);return;}
            frame=frame with {DamageCardOffer=draft with {Refusing=true,RequiredDiscardCount=count,SelectedDiscardIds=[]}};ReplaceRuntimeTop(frame);PublishDamageCardOffer(frame);return;
        }
        if(option!="discard" || !draft.Refusing) throw new InvalidOperationException("Invalid private offer branch.");
        var ids=(draft.SelectedDiscardIds??[]).Append(choice.Cards.Single()).ToArray();
        if(ids.Length<draft.RequiredDiscardCount){frame=frame with {DamageCardOffer=draft with {SelectedDiscardIds=ids}};ReplaceRuntimeTop(frame);ClearPendingDecision();PublishDamageCardOffer(frame);return;}
        if(ids.Length!=draft.RequiredDiscardCount || ids.Distinct().Count()!=ids.Length || ids.Any(id=>_cardZones.GetLocation(id) is not {OwnerSeat:{ } seat,Zone:CardZoneKind.Hand or CardZoneKind.Equipment} || seat!=draft.SourceSeat)) throw new InvalidOperationException("Private refusal cost changed.");
        ClearPendingDecision();ReplaceRuntimeTop(frame with {DamageCardOffer=null});
        MoveProgramCardsFromMultipleSources(ids,CardLocation.DiscardPile,new CardMoveReason($"skill-program.{frame.SkillId}.DiscardSelected"));
        AdvanceEventRulesAndQueueFact(new ProgramPrivateCardOfferResolvedEvent(frame.OwnerSeat,draft.SourceSeat,set.CardIds.Count,false,false));
        if(AwaitProgramBoundCardMovements(frame.Id,frame.OwnerSeat)==SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }
    private CardSnapshot[] GetOfferedPrivatelyViewedCards(int viewerSeat)
    {
        var frame=_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.DamageCardOffer?.SourceSeat==viewerSeat);
        if(frame?.DamageCardOffer is not { } draft) return [];
        var set=GetProgramCardSet(frame,draft.SourceBind);
        return set.CardIds.Select(id=>ToSnapshot(_cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c=>c.Id==id))).ToArray();
    }
    private void AssertPrivateOfferDrafts(ProgramSkillFrame frame,SkillProgramEffect paused)
    {
        if(frame.SelectedParticipantDiscard is { } discard)
        {
            var key="participant-discard-cursor-"+(frame.InstructionIndex-1);
            if(paused.Op!=SkillProgramEffectOp.DiscardSelectedParticipantCards || discard.Cursor<0 || discard.Cursor>=frame.SelectedTargetSeats.Count || frame.SelectedTargetSeats[discard.Cursor]!=discard.TargetSeat || frame.NumberBindings.SingleOrDefault(b=>b.Name==key)?.Value!=discard.Cursor+1 || frame.ReexecuteParticipantInstruction) throw new InvalidOperationException("Participant discard lost its instruction cursor.");
            if(ReferenceEquals(frame,_resolutionStack.LastOrDefault()) && (_pendingDecision?.PlayerSeat!=frame.OwnerSeat || !AssistedChoicesEqual(_pendingDecision.Choices,SelectedParticipantDiscardChoices(frame)))) throw new InvalidOperationException("Participant discard lost its chooser prompt.");
        }
        if(frame.DamageCardOffer is { } offer)
        {
            if(paused.Op!=SkillProgramEffectOp.OfferBoundCardsForDamagePrevention || paused.SourceBind!=offer.SourceBind || frame.WindowContext is not {Window:SkillProgramTriggerWindow.BeforeDamageApplied,SourceSeat:{ } source,TargetSeat:{ } target} || source!=offer.SourceSeat || target!=frame.OwnerSeat || source==target || IsDamageOfferPairBlocked(target,frame.SkillId,source)) throw new InvalidOperationException("Private offer lost its actual damage pair.");
            var set=GetProgramCardSet(frame,offer.SourceBind);
            if(set.CardIds.Select((id,i)=>(id,i)).Any(c=>set.SourceLocations[c.i].OwnerSeat!=frame.OwnerSeat || _cardZones.GetLocation(c.id)!=set.SourceLocations[c.i]) || offer.Refusing && (offer.RequiredDiscardCount<1 || offer.RequiredDiscardCount>set.CardIds.Count || offer.SelectedDiscardIds is null || offer.SelectedDiscardIds.Count>=offer.RequiredDiscardCount || offer.SelectedDiscardIds.Distinct().Count()!=offer.SelectedDiscardIds.Count || offer.SelectedDiscardIds.Any(id=>_cardZones.GetLocation(id) is not {OwnerSeat:{ } seat,Zone:CardZoneKind.Hand or CardZoneKind.Equipment} || seat!=source))) throw new InvalidOperationException("Private offer changed its real cards or unpaid refusal cost.");
            if(ReferenceEquals(frame,_resolutionStack.LastOrDefault()) && (_pendingDecision is not {IsPrivate:true} prompt || prompt.PlayerSeat!=offer.SourceSeat || !AssistedChoicesEqual(prompt.Choices,DamageCardOfferChoices(frame)))) throw new InvalidOperationException("Private offer lost its private source prompt.");
        }
    }
    private sealed partial class ProgramSkillHost : IPrivateOfferProgramEffectHost
    {
        public SkillProgramStepOutcome DiscardSelectedParticipantCards(ProgramSkillFrame frame)=>engine.DiscardProgramSelectedParticipantCards(frame);
        public SkillProgramStepOutcome OfferBoundCardsForDamagePrevention(ProgramSkillFrame frame,string bind)=>engine.OfferProgramBoundCardsForDamagePrevention(frame,bind);
    }
}
