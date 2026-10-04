namespace CardGame.Core;

public sealed record ProgramSourceFactionPreventionReceipt(int InstructionIndex, long BeforeDamageFrameId,
    long DamageOwnerFrameId, int SourceSeat, int TargetSeat, int PreventedAmount, string StateId,
    string SourceFactionId, string UsageId, long MovementSequenceBefore);
public sealed record ProgramSourceFactionPreventionIssuedEvent(long FrameId, long BeforeDamageFrameId,
    int SourceSeat, int TargetSeat, int PreventedAmount, CardConversionSource Source, string GameplayHash,
    string StateId, string SourceFactionId, string UsageId) : IGameEvent;
internal interface ISourceFactionPreventionProgramHost
{
    SkillProgramStepOutcome PreventDamageAndConsumeSourceFaction(ProgramSkillFrame frame, string stateId);
}
internal sealed class PreventDamageAndConsumeSourceFactionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventDamageAndConsumeSourceFaction;
    public override ISkillProgramEffectHandler Handler { get; } = new PreventDamageAndConsumeSourceFactionHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
public sealed class PreventDamageAndConsumeSourceFactionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PreventDamageAndConsumeSourceFaction;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ISourceFactionPreventionProgramHost)host).PreventDamageAndConsumeSourceFaction(frame, effect.StateId!);
}
