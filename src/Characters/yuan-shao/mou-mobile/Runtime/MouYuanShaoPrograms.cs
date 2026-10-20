namespace CardGame.Core;

public sealed record MouYuanShaoTurnStartedEvent(int TurnNumber, int TurnOwnerSeat, int Ordinal, long MovementBefore) : IGameEvent;
public sealed record MouYuanShaoDesignationEvent(long UseId, long ActionId, int TargetSeat,
    CardConversionSource? Source, string? GameplayHash, bool OtherQun) : IGameEvent;
public sealed record MouYuanShaoRewardReservedEvent(long FrameId, CardConversionSource Source, long WindowId,
    long UseId, long ActionId, int TargetSeat, MouYuanShaoTurnStartedEvent Turn) : IGameEvent;

internal interface IMouYuanShaoHost { SkillProgramStepOutcome Reserve(ProgramSkillFrame frame); }
internal sealed class ReserveMouYuanShaoRewardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReserveMouYuanShaoReward;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramTriggerWindow.CardResponseCompleted])];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new ReserveMouYuanShaoRewardHandler();
}
internal sealed class ReserveMouYuanShaoRewardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReserveMouYuanShaoReward;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMouYuanShaoHost>().Reserve(f);
}
