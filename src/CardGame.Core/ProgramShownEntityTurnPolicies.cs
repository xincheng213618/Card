namespace CardGame.Core;

public sealed record ShownEntityTurnPolicy(long ProgramFrameId,int EffectIndex,CardUseEffectSource Source,
    string GameplayHash,string StateId,int TurnNumber,int ActualTurnOwnerSeat,long ActualPlayStartingFrameId,
    int CardId,CardLocation OriginalFrom,Suit FrozenSuit,IReadOnlyList<int> AffectedSeats,IReadOnlyList<long> RestrictionSequences)
{
    private readonly IReadOnlyList<int> _affectedSeats=Array.AsReadOnly(AffectedSeats.ToArray());
    public IReadOnlyList<int> AffectedSeats {get=>_affectedSeats;init=>_affectedSeats=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<long> _restrictionSequences=Array.AsReadOnly(RestrictionSequences.ToArray());
    public IReadOnlyList<long> RestrictionSequences {get=>_restrictionSequences;init=>_restrictionSequences=Array.AsReadOnly(value.ToArray());}
}
public sealed record ShownEntityTurnPolicyGrantedEvent(ShownEntityTurnPolicy Policy):IGameEvent;
public sealed record ShownEntityUseBenefit(long CardUseFrameId,long ActionId,int OriginalActorSeat,int ProviderSeat,ShownEntityTurnPolicy Policy,IReadOnlyList<CardActionCost> PhysicalMaterials)
{
    private readonly IReadOnlyList<CardActionCost> _physicalMaterials=Array.AsReadOnly(PhysicalMaterials.ToArray());
    public IReadOnlyList<CardActionCost> PhysicalMaterials {get=>_physicalMaterials;init=>_physicalMaterials=Array.AsReadOnly(value.ToArray());}
}
// All other accepted material IDs remain only on the trusted owning frame.
// This fact publishes only the already-revealed entity and public policy.
public sealed record ShownEntityUseBenefitIssuedEvent(long CardUseFrameId,long ActionId,int OriginalActorSeat,int ProviderSeat,ShownEntityTurnPolicy Policy):IGameEvent;
internal interface IShownEntityTurnPolicyHost
{
    void IssueShownEntityTurnPolicy(ProgramSkillFrame frame,string sourceBind,string stateId);
}
internal sealed class IssueShownEntityTurnPolicyDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.IssueShownEntityTurnPolicy;
    public override ISkillProgramEffectHandler Handler {get;}=new IssueShownEntityTurnPolicyHandler();
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GrantTurnRuleModifier,static(_,c)=>c.ProhibitCurrentResponse());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","stateId","condition");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),
            sourceBind:r.RequiredIdentifier("sourceBind"),stateId:r.RequiredIdentifier("stateId"));
        RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting),new ReadSingleCardSet(e.SourceBind!)];
}
internal sealed class IssueShownEntityTurnPolicyHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.IssueShownEntityTurnPolicy;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)
    {((IShownEntityTurnPolicyHost)host).IssueShownEntityTurnPolicy(f,e.SourceBind!,e.StateId!);return SkillProgramStepOutcome.Continue;}
}
internal static class ShownEntityTurnPolicyComposition
{
    internal static void Validate(string path,IReadOnlyList<SkillProgramEffect> effects,SkillProgramTriggerWindow window,
        SkillProgramTriggerSubject? subject,SkillProgramTurnOwnerScope scope)
    {
        if(!effects.Any(e=>e.Op==SkillProgramEffectOp.IssueShownEntityTurnPolicy))return;
        if(window!=SkillProgramTriggerWindow.PlayPhaseStarting ||subject!=SkillProgramTriggerSubject.Owner ||scope!=SkillProgramTurnOwnerScope.Own ||
            effects is not [ {Op:SkillProgramEffectOp.SelectOwnedCards,Target:SkillProgramEffectTarget.Owner,Amount:1,ResultBind:{ } bind,Condition.Kind:SkillProgramConditionKind.Always} selected,
                {Op:SkillProgramEffectOp.RevealOwnedBoundCardAppearance,Target:SkillProgramEffectTarget.Owner,SourceBind:{ } shown,Condition.Kind:SkillProgramConditionKind.Always},
                {Op:SkillProgramEffectOp.IssueShownEntityTurnPolicy,SourceBind:{ } granted,Condition.Kind:SkillProgramConditionKind.Always}] ||
            bind!=shown ||bind!=granted ||!selected.Zones.SequenceEqual([CardZoneKind.Hand]) ||selected.CardKinds.Count!=0 ||selected.Suits.Count!=0 ||
            selected.TargetReference is not null ||selected.MinimumCards!=0 ||selected.MaximumCards!=0 ||selected.AllowDecline)
            throw new InvalidOperationException($"Invalid skill program at {path}: shown entity policy owns one exact Hand reveal at its own actual Play start.");
    }
}
