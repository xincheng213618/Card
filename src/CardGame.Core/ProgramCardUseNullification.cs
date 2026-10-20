using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The first issued whole-use ineffective state; costs and use windows remain valid.</summary>
public sealed record ProgramCardUseNullifiedEvent(
    long ProgramFrameId, long ParentUseFrameId, long WindowFrameId, long ActionId,
    string SkillId, string SkillInstanceId, string BindingId, string GameplayHash,
    int InstructionIndex, int OwnerSeat, int ActorSeat, CardKind EffectiveKind) : IGameEvent;

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCardUseNullifiedEvent? ProgramUseNullification { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BeforeSimpleTargetEffectsStarted { get; init; }
}

internal interface IWholeCardUseNullificationHost
{
    void NullifyCurrentCardUse(ProgramSkillFrame frame);
}

internal sealed class NullifyCurrentCardUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyCurrentCardUse;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ISkillProgramEffectHandler Handler { get; } = new NullifyCurrentCardUseHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.NullifyCurrentCardEffect, static (_, context) => context.NullifyCurrentCardUse());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseBeforeTargetEffects)];
}

public sealed class NullifyCurrentCardUseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyCurrentCardUse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IWholeCardUseNullificationHost>().NullifyCurrentCardUse(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
