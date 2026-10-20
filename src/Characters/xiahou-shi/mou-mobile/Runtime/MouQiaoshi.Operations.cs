using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouXiahouShiQiaoshiStage { SourceChoice, RecoveryChildren, Drawing, DrawChildren }
public sealed record MouXiahouShiQiaoshiReceipt(CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, long WindowId, long DamageFrameId, long AttackResolutionId,
    int DamageSourceSeat, int DamageAmount)
{
    public MouXiahouShiQiaoshiStage Stage { get; init; }
    public string? Branch { get; init; }
    public int RequestedRecovery { get; init; }
    public int BaseRecovery { get; init; }
    public int RecoveryHpBefore { get; init; }
    public bool RecoveryReturned { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record MouXiahouShiQiaoshiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, long WindowId, long DamageFrameId, long AttackResolutionId,
    int DamageSourceSeat, int DamageAmount) : IGameEvent;
public sealed record MouXiahouShiQiaoshiBranchAcceptedEvent(long FrameId, CardConversionSource Source,
    MouXiahouShiTurnKey Turn, int DamageSourceSeat, string Branch) : IGameEvent;
public sealed record MouXiahouShiQiaoshiRecoveryIssuedEvent(long FrameId, int SourceSeat, int TargetSeat,
    int DamageAmount, int RequestedRecovery, int HpBefore, int BaseRecovery) : IGameEvent;
public sealed record MouXiahouShiQiaoshiRecoveryReturnedEvent(long FrameId, int SourceSeat, int TargetSeat,
    int RequestedRecovery) : IGameEvent;
internal sealed class ResolveMouXiahouShiQiaoshiDescriptor : MouXiahouShiDescriptor
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new RequireContext(ProgramContextCapability.Damage)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiQiaoshiHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouXiahouShiQiaoshiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Qiaoshi(f);
}
