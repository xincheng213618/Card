using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouLixianStage { NameChoice, MaterialChoice, TargetChoice, NativeIssuing, NativeUse, NativeChildren }
public enum MouBingcaiStage { MaterialChoice, CostChildren, Drawing, DrawChildren, AddNameChoice }

public sealed record MouLixianReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long OriginFrameId)
{
    public MouLixianStage Stage { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long MovementSequence { get; init; }
    public CardKind? OutputKind { get; init; }
    public int? MaterialId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public CardLocation? From { get; init; }
    public Suit EffectiveSuit { get; init; }
    public int? EffectiveRank { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Paid { get; init; }
    public bool NativeReturned { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<long> _nativeMovementBatchIds = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeMovementBatchIds { get => _nativeMovementBatchIds; init => _nativeMovementBatchIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed record MouBingcaiReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId, CardKind BasicKind)
{
    public MouBingcaiStage Stage { get; init; }
    public int? MaterialId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public CardKind? EffectiveKind { get; init; }
    public CardLocation? From { get; init; }
    public Suit EffectiveSuit { get; init; }
    public int? EffectiveRank { get; init; }
    public bool Paid { get; init; }
    public bool MatchesDamage { get; init; }
    public bool IsTrick { get; init; }
    public long RecastInvocationId { get; init; }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long MovementSequence { get; init; }
    public long DrawAttemptFrameId { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    private readonly IReadOnlyList<int> _actualDrawIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ActualDrawIds { get => _actualDrawIds; init => _actualDrawIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _replacementGainedIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ReplacementGainedIds { get => _replacementGainedIds; init => _replacementGainedIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLixianReceipt? MouLixian { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouBingcaiReceipt? MouBingcai { get; init; }
}

public sealed record MouLixianStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramTriggerWindow Window, long OriginFrameId, ActualDiscardRecoveryPhaseKey? Phase) : IGameEvent;
public sealed record MouLixianMaterialSelectedEvent(long FrameId, CardKind OutputKind, int CardId, CardKind PrintedKind, CardLocation From, Suit EffectiveSuit, int? EffectiveRank) : IGameEvent;
public sealed record MouLixianUseIssuedEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, CardKind OutputKind, int CardId, CardKind PrintedKind, CardLocation From, Suit EffectiveSuit, int? EffectiveRank) : IGameEvent;
public sealed record MouLixianUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouLixianCompletedEvent(long FrameId, long UseId, bool Used) : IGameEvent;
public sealed record MouLixianNameUsedEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, CardKind OutputKind) : IGameEvent;
public sealed record MouLixianNameAddedEvent(long FrameId, int OwnerSeat, CardKind OutputKind, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouBingcaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId, CardKind BasicKind) : IGameEvent;
public sealed record MouBingcaiRecastPaidEvent(long FrameId, long RecastInvocationId, long BatchId, int OwnerSeat, int CardId, CardKind PrintedKind, CardKind EffectiveKind, CardLocation From, long MovementSequence, long Before, long After, bool MatchesDamage, bool IsTrick) : IGameEvent;
public sealed record MouBingcaiDrawReturnedEvent(long FrameId, long AttemptFrameId, long SequenceBefore, long SequenceAfter, int ActualDrawCount, int ReplacementGainedCount) : IGameEvent;
public sealed record MouBingcaiCompletedEvent(long FrameId, long UseId, bool Recast, bool Matched, bool AddedName) : IGameEvent;

internal interface IMouHuangYueyingHost
{
    SkillProgramStepOutcome ResolveMouHuangYueyingLixian(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveMouHuangYueyingBingcai(ProgramSkillFrame frame);
}
internal sealed class MouLixianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingLixian;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLixianHandler();
}
internal sealed class MouLixianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingLixian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouHuangYueyingHost>().ResolveMouHuangYueyingLixian(frame);
}
internal sealed class MouBingcaiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingBingcai;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouBingcaiHandler();
}
internal sealed class MouBingcaiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingBingcai;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouHuangYueyingHost>().ResolveMouHuangYueyingBingcai(frame);
}

public sealed record MouLixianNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, CardActionContext OriginalAction, int CardId, CardKind PrintedKind, CardLocation From, long BeforePayment);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLixianNativeOrigin? MouLixianOrigin { get; init; }
}
public sealed record MouLixianMaterialPaidEvent(long FrameId, long UseId, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record MouLixianNativeBatchSignedEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
public sealed record MouLixianNativeMovementSignedEvent(long FrameId, long UseId, long BatchId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, CardMoveReason Reason, long MovementSequence) : IGameEvent;
