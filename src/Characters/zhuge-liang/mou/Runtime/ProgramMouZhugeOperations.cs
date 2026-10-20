namespace CardGame.Core;

internal interface IMouZhugeProgramHost
{
    SkillProgramStepOutcome ExecuteMouZhuge(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class MouZhugeOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new MouZhugeOperationHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.MouWujingDraw
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.MouWujingDraw ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived),
            new RequireContext(ProgramContextCapability.MovementSource)] : [new RequireActivationEntry()];
}
internal sealed class MouZhugeOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<IMouZhugeProgramHost>().ExecuteMouZhuge(frame, effect);
}
public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhijueDraft? MouZhijue { get; init; }
}
public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhijueUseReturn? MouZhijueReturn { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhitianFirePayment? MouZhitianPayment { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeMouZhitianFireFrameId { get; init; }
}
