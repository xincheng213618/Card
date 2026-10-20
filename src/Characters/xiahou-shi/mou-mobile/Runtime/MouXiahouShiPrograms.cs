using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouXiahouShiTurnKey(int TurnNumber, int OwnerSeat, int Ordinal);
public enum MouXiahouShiYanyuStage { Discarding, DiscardChildren, TargetChoice, Drawing, DrawChildren }
public enum MouXiahouShiQiaoshiStage { SourceChoice, RecoveryChildren, Drawing, DrawChildren }
public sealed record MouXiahouShiDiscardPayment(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence);
public sealed record MouXiahouShiYanyuReceipt(CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, bool Support)
{
    public MouXiahouShiYanyuStage Stage { get; init; }
    public long WindowId { get; init; }
    public MouXiahouShiDiscardPayment? Payment { get; init; }
    public int FrozenDiscardCount { get; init; }
    public int? TargetSeat { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
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
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXiahouShiYanyuReceipt? MouXiahouShiYanyu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXiahouShiQiaoshiReceipt? MouXiahouShiQiaoshi { get; init; }
}
public sealed record MouXiahouShiYanyuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, bool Support, long WindowId, int FrozenDiscardCount) : IGameEvent;
public sealed record MouXiahouShiYanyuDiscardPaidEvent(long FrameId, CardConversionSource Source,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouXiahouShiYanyuTargetAcceptedEvent(long FrameId, int TargetSeat, int FrozenDiscardCount, int RequestedDraw) : IGameEvent;
public sealed record MouXiahouShiQiaoshiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, long WindowId, long DamageFrameId, long AttackResolutionId,
    int DamageSourceSeat, int DamageAmount) : IGameEvent;
public sealed record MouXiahouShiQiaoshiBranchAcceptedEvent(long FrameId, CardConversionSource Source,
    MouXiahouShiTurnKey Turn, int DamageSourceSeat, string Branch) : IGameEvent;
public sealed record MouXiahouShiQiaoshiRecoveryIssuedEvent(long FrameId, int SourceSeat, int TargetSeat,
    int DamageAmount, int RequestedRecovery, int HpBefore, int BaseRecovery) : IGameEvent;
public sealed record MouXiahouShiQiaoshiRecoveryReturnedEvent(long FrameId, int SourceSeat, int TargetSeat,
    int RequestedRecovery) : IGameEvent;
public sealed record MouXiahouShiDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount, int ActualCount,
    int ReplacementCount, long SequenceBefore, long SequenceAfter, long? AttemptId) : IGameEvent;
public sealed record MouXiahouShiCompletedEvent(long FrameId, string SkillId, bool Accepted) : IGameEvent;
public sealed record MouXiahouShiMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From,
    CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IMouXiahouShiHost
{
    SkillProgramStepOutcome Discard(ProgramSkillFrame frame);
    SkillProgramStepOutcome Support(ProgramSkillFrame frame);
    SkillProgramStepOutcome Qiaoshi(ProgramSkillFrame frame);
}
internal abstract class MouXiahouShiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ResolveMouXiahouShiYanyuDiscardDescriptor : MouXiahouShiDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected, static (e, c) =>
    { c.DiscardSelected(new(SkillProgramEffectOp.DiscardSelected, SkillProgramEffectTarget.Owner, 1, e.Condition)); c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)); c.PublicControlValue(3); });
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiDiscardHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveMouXiahouShiYanyuSupportDescriptor : MouXiahouShiDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiSupportHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveMouXiahouShiQiaoshiDescriptor : MouXiahouShiDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new RequireContext(ProgramContextCapability.Damage)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiQiaoshiHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouXiahouShiDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Discard(f);
}
internal sealed class MouXiahouShiSupportHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Support(f);
}
internal sealed class MouXiahouShiQiaoshiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Qiaoshi(f);
}
