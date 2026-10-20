using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouYuJinXiayuanReceipt(CardConversionSource Source, string GameplayHash,
    long OriginalDamageWindowId, DamageArmorPaymentReceipt OriginalPayment, int RoundNumber, int TurnNumber,
    int MovementBefore, bool CostReturned = false, bool Completed = false);
public sealed record MouYuJinXiayuanIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalDamageWindowId, DamageArmorPaymentReceipt OriginalPayment, int RoundNumber, int TurnNumber, int MovementBefore) : IGameEvent;
public sealed record MouYuJinXiayuanCostReturnedEvent(long FrameId, long InvocationId, int ActualHandCount) : IGameEvent;
public sealed record MouYuJinXiayuanCompletedEvent(long FrameId, int TargetSeat, int RestoredAmount) : IGameEvent;
internal sealed class ResolveMouYuJinXiayuanDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouYuJinXiayuan;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantArmor, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouYuJinHandler(SkillProgramEffectOp.ResolveMouYuJinXiayuan);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
