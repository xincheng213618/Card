using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouLiuXieStage { LordExclusions, OwnerCount, SelectingDiscard, DiscardChildren, Drawing, DrawChildren, Adjust, Rewards, Damage }
public enum MouLiuXieAdjustmentKind { None, Same, Draw, Discard }
public sealed record MouLiuXieAdjustment(int Seat, MouLiuXieAdjustmentKind Kind, int ActualCount);
public sealed record MouLiuXieReceipt(CardConversionSource Source, string GameplayHash, int Instruction,
    int ActualTurn, ActualDiscardRecoveryPhaseKey Phase)
{
    public MouLiuXieStage Stage { get; init; }
    public MouLiuXieStage NextStage { get; init; }
    public int TargetCount { get; init; } = -1;
    public int Cursor { get; init; }
    public int RewardCursor { get; init; }
    public int Payer { get; init; } = -1;
    public int Requested { get; init; }
    public bool IsAdjustment { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<int> _participants = [], _lordCandidates = [], _excluded = [], _selected = [], _paying = [];
    private IReadOnlyList<MouLiuXieAdjustment> _adjustments = [];
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> LordCandidates { get => _lordCandidates; init => _lordCandidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Excluded { get => _excluded; init => _excluded = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Paying { get => _paying; init => _paying = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouLiuXieAdjustment> Adjustments { get => _adjustments; init => _adjustments = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLiuXieReceipt? MouLiuXie { get; init; }
}
public sealed record MouLiuXieStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Instruction,
    int ActualTurn, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLiuXieExcludedEvent(long FrameId, int Seat) : IGameEvent;
public sealed record MouLiuXieTargetCountEvent(long FrameId, int Count) : IGameEvent;
public sealed record MouLiuXieDiscardPaidEvent(long FrameId, int Payer, int CardId, long Sequence, bool Adjustment) : IGameEvent;
public sealed record MouLiuXieDrawReturnedEvent(long FrameId, int Recipient, int Requested, int Actual, int Replacement, long? AttemptId, bool Adjustment) : IGameEvent;
public sealed record MouLiuXieAdjustedEvent(long FrameId, int Seat, MouLiuXieAdjustmentKind Kind, int ActualCount) : IGameEvent;
public sealed record MouLiuXieDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat) : IGameEvent;
public sealed record MouLiuXieCompletedEvent(long FrameId) : IGameEvent;

internal interface IMouLiuXieRuntime : IProgramRuntimeOperations
{
    SkillProgramStepOutcome Start(ProgramSkillFrame frame);
    SkillProgramStepOutcome PrepareChensheng(ProgramSkillFrame frame);
}
internal sealed class ResolveMouLiuXieZhanbanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLiuXieZhanban;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouLiuXieHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class PrepareMouLiuXieChenshengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareMouLiuXieChensheng;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouLiuXieHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class MouLiuXieHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        operation == SkillProgramEffectOp.ResolveMouLiuXieZhanban ? host.GetCapability<IMouLiuXieRuntime>().Start(frame) : host.GetCapability<IMouLiuXieRuntime>().PrepareChensheng(frame);
}
