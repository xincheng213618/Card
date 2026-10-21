using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>An accepted empty-material Use; its source remains valid after the issuer dies or loses the skill.</summary>
public sealed record OriginalUseLightningOrigin(long UseId, long ActionId, CardConversionSource Source, string GameplayHash);

/// <summary>A held delayed effect. It never owns, counts as, or moves a physical card.</summary>
public sealed record OriginalUseLightningAttachment(long Id, OriginalUseLightningOrigin Origin,
    int OriginalTarget, int Holder, long AfterMovement, long Order, int PlacedTurn, int LastJudgmentTurn = -1);

public sealed partial class CharacterState
{
    private IReadOnlyList<OriginalUseLightningAttachment> _heldOriginalUseLightning = [];
    internal IReadOnlyList<OriginalUseLightningAttachment> HeldOriginalUseLightning
    {
        get => _heldOriginalUseLightning;
        set => _heldOriginalUseLightning = Array.AsReadOnly(value.ToArray());
    }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginalUseLightningOrigin? OriginalUseLightning { get; init; }
}

public sealed record NativeDelayedAttackChildReturn(long JudgmentId, long ChildFrameId);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDelayedAttackChildReturn? NativeDelayedAttackReturn { get; init; }
}

public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginalUseLightningAttachment? OriginalUseLightning { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginalUseLightningExcludedCardId { get; init; }
    // Recovery replays accepted commands; trusted frame diagnostics also retain the exact native receipt.
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal GameEngine.AttackCompletionReceipt? NativeDelayedAttackCompletion { get; init; }
}

public sealed partial record DelayedEffectContinuation
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OriginalUseLightningAttachment? OriginalUseLightning { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginalUseLightningExcludedCardId { get; init; }
}

public sealed record OriginalUseLightningIssuedEvent(OriginalUseLightningOrigin Origin) : IGameEvent;
public sealed record OriginalUseLightningPlacedEvent(OriginalUseLightningAttachment Attachment) : IGameEvent;
public sealed record OriginalUseLightningVisitedEvent(long AttachmentId, int Holder, int Turn) : IGameEvent;
public sealed record OriginalUseLightningTransferredEvent(long AttachmentId, int From, int To, long AfterMovement, long Order) : IGameEvent;
public sealed record OriginalUseLightningRemovedEvent(long AttachmentId, int Holder, string Reason) : IGameEvent;
public sealed record OriginalUseLightningJudgmentEvent(long AttachmentId, long JudgmentId, int Holder, int Turn) : IGameEvent;
public sealed record OriginalUseLightningResultEvent(long AttachmentId, long JudgmentId, int Holder, bool Hit,
    int? JudgmentCardId, int? NextHolder) : IGameEvent;
