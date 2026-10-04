using System.Collections.ObjectModel;
namespace CardGame.Core;

public interface IParticipantPhaseProgramEffectHost
{
    void ConsumeTargetPhaseLedger(ProgramSkillFrame frame, string usageId);
    void GrantTurnBoundSuitUseProhibition(ProgramSkillFrame frame, string sourceBind);
}
internal sealed class ConsumeTargetPhaseLedgerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumeTargetPhaseLedger;
    public override ISkillProgramEffectHandler Handler { get; } = new ConsumeTargetPhaseLedgerHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.CaptureSelectedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","usageId","condition");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),stateId:r.RequiredIdentifier("usageId"));
        RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
public sealed class ConsumeTargetPhaseLedgerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumeTargetPhaseLedger;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)
    { ((IParticipantPhaseProgramEffectHost)host).ConsumeTargetPhaseLedger(frame,effect.StateId!);return SkillProgramStepOutcome.Continue; }
}
internal sealed class GrantTurnBoundSuitUseProhibitionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnBoundSuitUseProhibition;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnBoundSuitUseProhibitionHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardActionProhibition,static (_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","condition");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadCardSet(effect.SourceBind!)];
}
public sealed class GrantTurnBoundSuitUseProhibitionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnBoundSuitUseProhibition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)
    { ((IParticipantPhaseProgramEffectHost)host).GrantTurnBoundSuitUseProhibition(frame,effect.SourceBind!);return SkillProgramStepOutcome.Continue; }
}
public sealed partial class GameEngine
{
    private readonly List<Suit> _fullDiscardPhaseSuits=[];
    private int _fullDiscardPhaseSuitTurn=-1;
    private bool HasActuallyUsableHandCard(CharacterState owner) => BuildLegalActions(owner,includeProgramActions:false)
        .Any(action => action.CardId is { } id && _cardZones.GetLocation(id) == CardLocation.Hand(owner.Seat) &&
            action.Kind is not (LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill or LegalActionKind.EndPlay or LegalActionKind.Recast) ||
            action.ConversionSource is not null && action.MinCardCount > 1 && action.SelectableCardIds.Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(owner.Seat)) ||
            action.Kind == LegalActionKind.UseEquipmentEffect && action.EquipmentKind == CardKind.ZhangbaSerpentSpear &&
            GetZhangbaSlashPairs(owner).Any(pair => !IsTurnPhysicalUseForbidden(owner.Seat,pair.Select(c=>c.Id).ToArray())));
    private bool CanActivateTargetPhaseLedger(int owner,string skill,string usage,int target) => _players[target].IsAlive &&
        _skillRuntimeState.GetUsage(owner,skill,$"{usage}:target:{target}",SkillUsageScope.Phase)==0;
    private void ConsumeProgramTargetPhaseLedger(ProgramSkillFrame frame,string usage)
    {
        var active=GetActiveProgramFrame(frame.Id);
        var activation=ProgramInstructionResolver.Default.Resolve(_contentRegistry.GetSkill(frame.SkillId).Program!,ProgramInstructionSourceKind.Activation,frame.ActivationId).Activation!;
        if(active.TriggerId is not null || active.SelectedTargetSeats is not [var target] || activation.TargetPhaseLedgerId != usage ||
            !CanActivateTargetPhaseLedger(active.OwnerSeat,active.SkillId,usage,target)) throw new InvalidOperationException("Participant phase usage is unavailable.");
        var key=$"{usage}:target:{target}";
        if(!_skillRuntimeState.TryConsumeUsage(active.OwnerSeat,active.SkillId,key,SkillUsageScope.Phase,1)) throw new InvalidOperationException("Participant phase usage could not advance.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(active.OwnerSeat,active.SkillId,key,SkillUsageScope.Phase,1));
    }
    private bool IsTurnPhysicalUseForbidden(int seat, IReadOnlyList<int> ids) => IsHandCategoryMaterialRestricted(ids) || ids.Count > 0 && CaptureUsedCardSuit(seat, ids, _cardZones.CardsAt(_cardZones.GetLocation(ids[0])).Single(c => c.Id == ids[0])) is { } suit && IsTurnSuitUseForbidden(seat,suit);
    private bool IsTurnSuitUseForbidden(int seat,Suit suit) => _turnCardUseEffects.ActionProhibitions.Any(p=>p.TurnNumber==_turnNumber && p.TurnSeat==_currentSeat && p.Source.OwnerSeat==seat && p.Suits?.Contains(suit)==true && p.ActionTypes.Contains(CardActionType.Use));
    private void GrantProgramTurnBoundSuitUseProhibition(ProgramSkillFrame frame,string bind)
    {
        ValidateProgramTurnEffectGrant(frame);
        var active=GetActiveProgramFrame(frame.Id);var set=GetProgramCardSet(active,bind);
        if(set.CardIds is not [var id]) throw new InvalidOperationException("Suit restriction requires one chosen real card.");
        var location=_cardZones.GetLocation(id);
        if(location!=CardLocation.DiscardPile) throw new InvalidOperationException("Suit restriction requires completed real discard payment.");
        var card=_cardZones.CardsAt(location).Single(c=>c.Id==id);
        var suit=set.FrozenRevealedSuit ?? card.Suit;
        var granted=_turnCardUseEffects.GrantActionProhibition(_turnNumber,_currentSeat,frame.Id,frame.InstructionIndex-1,CreateProgramTurnEffectSource(frame),[],[CardActionType.Use],[suit]);
        AdvanceEventRulesAndQueueFact(new CardActionProhibitionGrantedEvent(granted));
        ReplaceRuntimeTop(active with { CardSetBindings=Array.AsReadOnly(active.CardSetBindings.Select(b=>b.Name==bind ? b with { Visibility=SkillProgramCardSetVisibility.Public,FrozenRevealedSuit=suit }:b).ToArray()) });
    }
    private void CollectFullDiscardPhaseSuit(Card card,CardMovementRecord move)
    {
        if(_phase!=TurnPhase.Discard || GetProgramDiscardSource(move) is not { OwnerSeat: { } owner } || owner!=_currentSeat) return;
        if(_fullDiscardPhaseSuitTurn!=_turnNumber){_fullDiscardPhaseSuitTurn=_turnNumber;_fullDiscardPhaseSuits.Clear();}
        _fullDiscardPhaseSuits.Add(EffectiveSuit(_players[owner],card));
    }
    private bool FullDiscardPhaseSuitsAllDistinct => _fullDiscardPhaseSuitTurn==_turnNumber && _fullDiscardPhaseSuits.Count>=2 && _fullDiscardPhaseSuits.Distinct().Count()==_fullDiscardPhaseSuits.Count;
    private sealed partial class ProgramSkillHost : IParticipantPhaseProgramEffectHost
    {
        public void ConsumeTargetPhaseLedger(ProgramSkillFrame frame,string usageId)=>engine.ConsumeProgramTargetPhaseLedger(frame,usageId);
        public void GrantTurnBoundSuitUseProhibition(ProgramSkillFrame frame,string sourceBind)=>engine.GrantProgramTurnBoundSuitUseProhibition(frame,sourceBind);
    }
}
