using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>A real draw request, frozen before any DrawPile material moves.</summary>
public sealed record NativeDrawAttemptContext(long AttemptFrameId, long ParentFrameId,
    int RecipientSeat, int RequestedCount, CardMoveReason Reason, int ActualTurnNumber,
    int ActualTurnOwnerSeat, ActualDiscardRecoveryPhaseKey? ActualPhase, long SequenceBefore);

public enum NativeDrawAttemptStage { Programs, Returning }
public enum NativeDrawAttemptReturnKind { ProgramInstruction, ProgramOperation, DrawPhase, CardUseDrawTwo, SoleIncomingRange = 1243, SkillInvocation = 38700, KemingSkillEcho = 39700, EquipmentRecast = 40081, WumiaoFlood = 28510, DragonBoatCardEffect = 43000, GuanduCardEffect = 45000 }
public sealed record NativeDrawAttemptReturn(NativeDrawAttemptReturnKind Kind, int InstructionIndex,
    int RecipientIndex);

/// <summary>Replacement gains are never the physical result of a draw.</summary>
public enum NativeDrawReplacementSourceKind { OwnedHandOrEquipment, DrawPileSlash }

/// <summary>The issued rule source is retained even after its owner or skill disappears.</summary>
public sealed record NativeDrawPileSlashProof(long ProgramFrameId, long EntitlementFrameId,
    CardConversionSource EntitlementSource, string EntitlementGameplayHash);

public sealed record NativeDrawReplacementResult(int OwnerSeat, string SkillId, string SkillInstanceId,
    string BindingId, int SourceSeat, CardMoveReason Reason, long SequenceBefore, long SequenceAfter)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NativeDrawReplacementSourceKind SourceKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawPileSlashProof? DrawPileSlashProof { get; init; }
    private readonly IReadOnlyList<int> _gainedIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GainedIds
    { get => _gainedIds; init => _gainedIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed record NativeDrawAttemptFrame(long Id, NativeDrawAttemptContext Context,
    NativeDrawAttemptReturn Return, long ActiveWindowFrameId,
    NativeDrawAttemptStage Stage = NativeDrawAttemptStage.Programs,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.NativeDrawAttempt, Step)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawReplacementResult? Replacement { get; init; }
}

public sealed record NativeDrawAttemptStartedEvent(NativeDrawAttemptContext Context,
    NativeDrawAttemptReturn Return) : IGameEvent;

public sealed record NativeDrawAttemptDecidedEvent(long AttemptFrameId, long ParentFrameId,
    int RecipientSeat, int RequestedCount, bool Replaced) : IGameEvent;

/// <summary>The paid producer owns its recipient cursor and exact child return.</summary>
public sealed record NativeDrawProducerReceipt(int InstructionIndex, int RequestedCount,
    string? ResultBind, SkillProgramCardSetVisibility Visibility, CardMoveReason Reason)
{
    public NativeDrawAttemptReturnKind ReturnKind { get; init; }
    public SkillProgramEffectOp? Operation { get; init; }
    private readonly IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Recipients
    { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    public int RecipientIndex { get; init; }
    public long? ActiveAttemptId { get; init; }
    public NativeDrawReplacementResult? Replacement { get; init; }
    public bool AttemptReturned { get; init; }
    public bool RecipientApplied { get; init; }
}

public sealed record NativeDrawOutcome(int RecipientSeat, int RequestedCount, long? AttemptFrameId,
    long SequenceBefore, long SequenceAfter, NativeDrawReplacementResult? Replacement)
{
    private readonly IReadOnlyList<int> _actualDrawIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> ActualDrawIds
    { get => _actualDrawIds; init => _actualDrawIds = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore] public IReadOnlyList<int> ReplacementGainedIds => Replacement?.GainedIds ?? Array.AsReadOnly(Array.Empty<int>());
}

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawProducerReceipt? NativeDraw { get; init; }
}

public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawAttemptContext? NativeDraw { get; init; }
}

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawAttemptContext? NativeDrawAttempt { get; init; }
}

/// <summary>Optional adapter: third-party primitive hosts retain their synchronous Draw API.</summary>
internal interface IResumableProgramDrawHost
{
    SkillProgramStepOutcome DrawInstruction(ProgramSkillFrame frame, DrawProgramInstruction instruction,
        int targetSeat, CardMoveReason reason);
}
