using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouLiuChengAccrualClaim(CardConversionSource Source, string GameplayHash, long UseId,
    long ActionId, int ActorSeat, CardKind EffectiveKind, ActualDiscardRecoveryPhaseKey Phase, int NotificationOrdinal)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public bool Equals(MouLiuChengAccrualClaim? other) => other is not null && Source == other.Source &&
        GameplayHash == other.GameplayHash && UseId == other.UseId && ActionId == other.ActionId && ActorSeat == other.ActorSeat &&
        EffectiveKind == other.EffectiveKind && Phase == other.Phase && NotificationOrdinal == other.NotificationOrdinal && Targets.SequenceEqual(other.Targets);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Source); hash.Add(GameplayHash); hash.Add(UseId); hash.Add(ActionId); hash.Add(ActorSeat);
        hash.Add(EffectiveKind); hash.Add(Phase); hash.Add(NotificationOrdinal); foreach (var target in Targets) hash.Add(target); return hash.ToHashCode();
    }
}
public sealed record MouLiuChengUseClaim(CardConversionSource Source, string GameplayHash, long NativeParentId,
    long ActionId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind, int ActualTurn, int TurnOwner, long MovementSequence);
public enum MouLiuChengStage { Drawing, DrawChildren, UseChoice, NativeIssuing, NativeUse, NativeChildren }
public sealed record MouLiuChengUsePlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId,
    CardKind? RequiredKind, string Description, bool Adjusted = false)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLiuChengCompletionReceipt(MouLiuChengUseClaim Claim, long WindowId)
{
    public MouLiuChengStage Stage { get; init; }
    public bool Paid { get; init; }
    public CardKind OutputKind { get; init; }
    public int MarkBefore { get; init; }
    public int MarkAfter { get; init; }
    public long PaidSequence { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public MouLiuChengUsePlan? Plan { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    private readonly IReadOnlyList<long> _nativeBatchIds = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatchIds { get => _nativeBatchIds; init => _nativeBatchIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLiuChengNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, CardKind Kind);
public abstract partial record ResolutionFrame
{
    private readonly IReadOnlyList<MouLiuChengUseClaim>? _mouLiuChengClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MouLiuChengUseClaim>? MouLiuChengClaims
    { get => _mouLiuChengClaims; init => _mouLiuChengClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<int>? _mouLiuChengAnnouncedTargets;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<int>? MouLiuChengAnnouncedTargets
    { get => _mouLiuChengAnnouncedTargets; init => _mouLiuChengAnnouncedTargets = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<MouLiuChengAccrualClaim>? _mouLiuChengAccrualClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MouLiuChengAccrualClaim>? MouLiuChengAccrualClaims
    { get => _mouLiuChengAccrualClaims; init => _mouLiuChengAccrualClaims = value is null ? null : Array.AsReadOnly(value.Select(c => c with { Targets = c.Targets }).ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLiuChengNativeOrigin? MouLiuChengOrigin { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLiuChengAccrualClaim? MouLiuChengAccrual { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLiuChengCompletionReceipt? MouLiuChengCompletion { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLiuChengAccrualClaim? MouLiuChengAccrual { get; init; } }
public sealed record MouLiuChengTargetAnnouncedEvent(long UseId, long ActionId, int NotificationOrdinal, int TargetSeat) : IGameEvent;
public sealed record MouLiuChengAccrualQualifiedEvent(long UseId, long ActionId, int NotificationOrdinal,
    CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int TargetCount) : IGameEvent;
public sealed record MouLiuChengAccrualStartedEvent(long FrameId, CardConversionSource Source, long UseId, long ActionId, int NotificationOrdinal) : IGameEvent;
public sealed record MouLiuChengChuiEarnedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int NotificationOrdinal, int TargetSeat, ActualDiscardRecoveryPhaseKey Phase, int PhaseUsed, int Count) : IGameEvent;
public sealed record MouLiuChengCompletionQualifiedEvent(MouLiuChengUseClaim Claim) : IGameEvent;
public sealed record MouLiuChengCompletionStartedEvent(long FrameId, MouLiuChengUseClaim Claim, long WindowId, int MarkBefore, int MarkAfter) : IGameEvent;
public sealed record MouLiuChengDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount, int ReplacementCount,
    long SequenceBefore, long SequenceAfter, long? AttemptId) : IGameEvent;
public sealed record MouLiuChengPlanAcceptedEvent(long FrameId, CardKind Kind, LegalActionKind ActionKind,
    int? TargetCardId, CardKind? RequiredKind, bool Adjusted, int TargetCount) : IGameEvent;
public sealed record MouLiuChengTargetAcceptedEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record MouLiuChengNativeIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, CardKind Kind) : IGameEvent;
public sealed record MouLiuChengNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouLiuChengNativeBatchSignedEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
public sealed record MouLiuChengNativeMovementSignedEvent(long FrameId, long UseId, long BatchId, int CardId,
    CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouLiuChengParentAttackRestoredEvent(long FrameId, long UseId, long ParentUseId, long ParentActionId) : IGameEvent;
public sealed record MouLiuChengCompletedEvent(long FrameId, long OriginalActionId, bool UsedFollowup) : IGameEvent;

internal interface IMouLiuChengHost
{
    SkillProgramStepOutcome Accrue(ProgramSkillFrame frame);
    SkillProgramStepOutcome Complete(ProgramSkillFrame frame);
}
internal sealed class AccrueMouLiuChengChuiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AccrueMouLiuChengChui;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLiuChengHandler(SkillProgramEffectOp.AccrueMouLiuChengChui);
}
internal sealed class ResolveMouLiuChengCompletionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLiuChengCompletion;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLiuChengHandler(SkillProgramEffectOp.ResolveMouLiuChengCompletion);
}
internal sealed class MouLiuChengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        op == SkillProgramEffectOp.AccrueMouLiuChengChui ? h.GetCapability<IMouLiuChengHost>().Accrue(f) : h.GetCapability<IMouLiuChengHost>().Complete(f);
}
