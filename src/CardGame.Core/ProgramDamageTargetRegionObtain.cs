using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DamageTargetObtainMode { EntireSelectedZone }
public enum DamageTargetDuelDamageExpression { ObtainedCardCount }

public sealed partial class SkillProgramEffect
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DamageTargetObtainMode? ObtainMode { get; internal init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DamageTargetDuelDamageExpression? DuelDamageExpression { get; internal init; }
}

public sealed record DamageTargetRegionMaterial(int CardId, CardKind PrintedKind);

/// <summary>One chosen original zone and the actual atomic movement, retained through all gain children.</summary>
public sealed record ProgramDamageTargetRegionObtainReceipt(
    CardZoneKind SelectedZone, int RecipientSeat, long SequenceBefore, long SequenceAfter,
    long MovementBatchId, bool Paid)
{
    private readonly IReadOnlyList<DamageTargetRegionMaterial> _materials = [];
    private readonly IReadOnlyList<ProgramDamageTargetObtainReceipt> _movements = [];
    public IReadOnlyList<DamageTargetRegionMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<ProgramDamageTargetObtainReceipt> Movements
    { get => _movements; init => _movements = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public int ActualObtainedCount => Movements.Count(m => m.To == CardLocation.Hand(RecipientSeat));
    public bool ContainsEquipment => Movements.Any(m => m.To == CardLocation.Hand(RecipientSeat) && m.IsEquipment);
}

/// <summary>Trusted payment fact. Hidden source-hand entities are never projected as a player prompt.</summary>
public sealed record DamageTargetRegionObtainPaidEvent(
    long ProgramFrameId, CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DamageWindowId, long DamageFrameId, long OuterCardUseFrameId, long OuterActionId,
    int VictimSeat, ProgramDamageTargetRegionObtainReceipt Receipt) : IGameEvent;
