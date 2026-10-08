using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>A stable, method-scoped name ledger; reacquisition does not create another round.</summary>
public sealed record ProgramRoundDistinctBasicUsePolicy(string StateId, string LedgerId);

/// <summary>Issued identity only. Private materials remain in the original frozen native action.</summary>
public sealed record RoundDistinctBasicUseReceipt(
    CardConversionSource Source, string GameplayHash, string StateId, string LedgerId,
    int RoundNumber, int ActualTurnNumber, int ActualTurnOwnerSeat,
    long CardActionId, long OwnerFrameId, int ActorSeat, CardKind EffectiveKind,
    CardKind CanonicalName, bool FrozenIsRed, bool IsResponseUse,
    long? ParentActionId = null, int? OpponentSeat = null, long? DyingFrameId = null);

// Scalar-only: no material id, printed kind, private hand or storage contents.
public sealed record RoundDistinctBasicUseAcceptedEvent(RoundDistinctBasicUseReceipt Receipt) : IGameEvent;

public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<RoundDistinctBasicUseReceipt>? _roundDistinctBasicUses;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RoundDistinctBasicUsesIssued { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RoundDistinctBasicUseReceipt>? RoundDistinctBasicUses
    {
        get => _roundDistinctBasicUses;
        init => _roundDistinctBasicUses = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
