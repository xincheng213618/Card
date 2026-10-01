namespace CardGame.Core;

internal interface IFinalTargetTurnCountHost
{
    SkillProgramStepOutcome ExecuteFinalTargetTurnCount(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed record RequireNonEquipmentAction : ProgramResourceOperation;
internal abstract class FinalTargetTurnCountDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.FinalTargetTurnCount(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        var source = Op == SkillProgramEffectOp.DiscardHandToNamedTurnCount;
        if (source) reader.AllowOnly("op", "target", "skillIds", "condition");
        else reader.AllowOnly("op", "target", "condition");
        var skills = source ? reader.RequiredIdentifierArray("skillIds") : [];
        if (source && skills.Count != 1) throw new InvalidOperationException("Turn count requires one named source skill.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(), skillIds: skills);
        RequireAlways(effect, reader.Path); return effect;
    }
}
internal sealed class DiscardNonFinalTargetCardThenDrawDescriptor : FinalTargetTurnCountDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardNonFinalTargetCardThenDrawHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor(), new RequireNonEquipmentAction()];
}
internal sealed class DiscardHandToNamedTurnCountDescriptor : FinalTargetTurnCountDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHandToNamedTurnCount;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardHandToNamedTurnCountHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}
public abstract class FinalTargetTurnCountHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IFinalTargetTurnCountHost)host).ExecuteFinalTargetTurnCount(effect, frame);
}
public sealed class DiscardNonFinalTargetCardThenDrawHandler : FinalTargetTurnCountHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw; }
public sealed class DiscardHandToNamedTurnCountHandler : FinalTargetTurnCountHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardHandToNamedTurnCount; }
public sealed record NamedTurnCountFlowDraft(string Stage, int TargetSeat, int Required,
    IReadOnlyList<int> CardIds, IReadOnlyList<int> SelectedIds, long? ActionId = null);
public sealed record NamedTurnSkillChoiceCommittedEvent(int OwnerSeat, string SkillId, string SkillInstanceId,
    string TriggerId, int TurnNumber, long ActionId, long ParentFrameId, int TargetSeat) : IGameEvent;
