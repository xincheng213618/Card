using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MoZhangFeiColor { Green, Yellow, Red }
public enum MoZhangFeiStage { Drawing, DrawChildren, ColorEffect, EffectChildren, RecoveryChildren, Entering, GainChildren }
public sealed record MoZhangFeiReceipt(CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Op, long WindowId, int ActualTurn, int Round, MoZhangFeiStage Stage)
{
    public long ColorChangeId { get; init; }
    public int TargetSeat { get; init; } = -1;
    public MoZhangFeiColor Color { get; init; }
    public bool Modified { get; init; }
    public int Requested { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? DrawAttemptId { get; init; }
    public long? GreenGrantFrameId { get; init; }
    public int? RedRequestedHpLoss { get; init; }
    public int? RedHpBefore { get; init; }
    private readonly IReadOnlyList<int> _gained = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GainedIds { get => _gained; init => _gained = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoZhangFeiReceipt? MoZhangFei { get; init; }
}
public sealed record MoZhangFeiColorArrivedEvent(CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwner, HpChangeContext Change, MoZhangFeiColor Color, bool Modified) : IGameEvent;
public sealed record MoZhangFeiStartedEvent(long FrameId, MoZhangFeiReceipt Receipt) : IGameEvent;
public sealed record MoZhangFeiDrawReturnedEvent(long FrameId, NativeDrawOutcome Outcome) : IGameEvent;
public sealed record MoZhangFeiColorEffectIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat, MoZhangFeiColor Color, long ChangeId) : IGameEvent;
public sealed record MoZhangFeiRedLossRequestedEvent(long FrameId, int TargetSeat, int HpBefore, int RequestedAmount) : IGameEvent;
public sealed record MoZhangFeiChenshiEnteredEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Round, long DamageWindowId) : IGameEvent;
public sealed record MoZhangFeiGreenReplacementEvent(long FrameId, long GrantFrameId, long AttemptId,
    int RecipientSeat, int Requested, long Before, long After, int Count) : IGameEvent;
public sealed record MoZhangFeiGreenMaterialEvent(long FrameId, long AttemptId, int CardId,
    CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MoZhangFeiCompletedEvent(long FrameId, SkillProgramEffectOp Op) : IGameEvent;

internal interface IMoZhangFeiRuntime : IProgramRuntimeOperations
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class MoZhangFeiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveMoZhangFeiZhuohun ? SkillProgramTriggerWindow.AfterHealthChanged :
            Op == SkillProgramEffectOp.ResolveMoZhangFeiChenshi ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.NativeDrawStarting)];
    public override ISkillProgramEffectHandler Handler => new MoZhangFeiHandler(Op);
}
internal sealed class MoZhangFeiZhuohunDescriptor : MoZhangFeiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoZhangFeiZhuohun; }
internal sealed class MoZhangFeiChenshiDescriptor : MoZhangFeiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoZhangFeiChenshi;
    public override ProgramFactRequirements FactRequirements => new(Triggered:
        ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
}
internal sealed class MoZhangFeiGreenDescriptor : MoZhangFeiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoZhangFeiGreenDraw; }
internal sealed class MoZhangFeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMoZhangFeiRuntime>().Resolve(frame, op);
}
