namespace CardGame.Core;

internal sealed record ReadCompletedActivationDiscard(string Name) : ProgramResourceOperation;

internal interface IProgramTargetCommitHost
{
    SkillProgramStepOutcome SelectRelativeZoneDemandTarget(SkillProgramEffect effect, ProgramSkillFrame frame);
    SkillProgramStepOutcome DrawOnFirstProgramTargetEncounter(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed class SelectRelativeZoneDemandTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectRelativeZoneDemandTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectRelativeZoneDemandTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (effect, context) => context.RelativeZoneDemand(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCompletedActivationDiscard(effect.SourceBind!), new SelectSingleTarget(), new CreateChoiceResult(effect.ResultBind!, ["hand", "equipment"])];
}
internal sealed class DrawOnFirstProgramTargetEncounterDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawOnFirstProgramTargetEncounterHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceProgramId", "sourceActivationId", "amount", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
            skillIds: [r.RequiredIdentifier("sourceProgramId")], stateId: r.RequiredIdentifier("sourceActivationId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.ProgramTargetCommitted)];
}
public sealed class SelectRelativeZoneDemandTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectRelativeZoneDemandTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => ((IProgramTargetCommitHost)host).SelectRelativeZoneDemandTarget(effect, frame);
}
public sealed class DrawOnFirstProgramTargetEncounterHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => ((IProgramTargetCommitHost)host).DrawOnFirstProgramTargetEncounter(effect, frame);
}
public sealed record ProgramRelativeZoneCandidate(int TargetSeat, string Mode);
public sealed record ProgramRelativeZoneDemand(string ResultBind, IReadOnlyList<ProgramRelativeZoneCandidate> Candidates, ProgramTargetCommitContext? Commit = null);
/// <summary>Public declaration identity; never a physical or virtual card use.</summary>
public sealed record ProgramTargetCommitContext(long ParentProgramFrameId, int OwnerSeat, string SourceProgramId,
    string SourceActivationId, string SourceSkillInstanceId, int TargetSeat, string Mode, int DeclarationOrdinal, bool FirstEncounter);
public sealed record ProgramTargetCommittedEvent(ProgramTargetCommitContext Declaration) : IGameEvent;
