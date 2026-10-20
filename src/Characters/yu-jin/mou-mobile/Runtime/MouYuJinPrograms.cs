using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouYuJinXiayuanReceipt(CardConversionSource Source, string GameplayHash,
    long OriginalDamageWindowId, DamageArmorPaymentReceipt OriginalPayment, int RoundNumber, int TurnNumber,
    int MovementBefore, bool CostReturned = false, bool Completed = false);
public enum MouYuJinJieyueStage { ArmorReserved, GiftChoice, GiftChildren, Complete }
public sealed record MouYuJinJieyueReceipt(CardConversionSource Source, string GameplayHash,
    long OriginalEndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat,
    MouYuJinJieyueStage Stage = MouYuJinJieyueStage.ArmorReserved, int? GiftCardId = null,
    CardLocation? GiftFrom = null, CardLocation? GiftTo = null, int? GiftMovementSequence = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouYuJinXiayuanReceipt? MouYuJinXiayuan { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouYuJinJieyueReceipt? MouYuJinJieyue { get; init; }
}
public sealed record MouYuJinXiayuanIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalDamageWindowId, DamageArmorPaymentReceipt OriginalPayment, int RoundNumber, int TurnNumber, int MovementBefore) : IGameEvent;
public sealed record MouYuJinXiayuanCostReturnedEvent(long FrameId, long InvocationId, int ActualHandCount) : IGameEvent;
public sealed record MouYuJinXiayuanCompletedEvent(long FrameId, int TargetSeat, int RestoredAmount) : IGameEvent;
public sealed record MouYuJinJieyueReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalEndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat) : IGameEvent;
public sealed record MouYuJinJieyueGiftPaidEvent(long FrameId, int RecipientSeat, int CardId,
    CardLocation From, CardLocation To, int MovementSequence) : IGameEvent;
public sealed record MouYuJinJieyueGiftDeclinedEvent(long FrameId, int RecipientSeat) : IGameEvent;
public sealed record MouYuJinJieyueCompletedEvent(long FrameId) : IGameEvent;

internal interface IMouYuJinHost
{
    SkillProgramStepOutcome Xiayuan(ProgramSkillFrame frame);
    SkillProgramStepOutcome Jieyue(ProgramSkillFrame frame);
}
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
internal sealed class ResolveMouYuJinJieyueDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouYuJinJieyue;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouYuJinHandler(SkillProgramEffectOp.ResolveMouYuJinJieyue);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouYuJinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveMouYuJinXiayuan ? host.GetCapability<IMouYuJinHost>().Xiayuan(f) : host.GetCapability<IMouYuJinHost>().Jieyue(f);
}
