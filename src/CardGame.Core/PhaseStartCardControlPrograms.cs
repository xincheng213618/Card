using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PhaseStartCardControlKind { OtherPlayRestriction, StartingEquipment }
public enum PhaseStartCardControlStage { Choosing, MovementChildren, GrantChildren, Completed }
public sealed record PhaseStartCardControlPayment(int CardId, CardKind Kind, Suit EffectiveSuit,
    CardLocation From, CardLocation To, long SequenceBefore, long SequenceAfter);
public sealed record PhaseStartCardControlReceipt(CardConversionSource Source, string GameplayHash,
    long ProgramFrameId, int InstructionIndex, long PhaseParentFrameId, int ActualTurnNumber,
    int ActualTurnOwnerSeat, int PhaseInstanceId, PhaseStartCardControlKind Kind,
    PhaseStartCardControlStage Stage, string? GrantedSkillId = null,
    PhaseStartCardControlPayment? Payment = null, bool ArmorAcquired = false);
public sealed record PhaseStartCardControlIssuedEvent(PhaseStartCardControlReceipt Receipt) : IGameEvent;
public sealed record PhaseStartCardControlPaidEvent(PhaseStartCardControlReceipt Receipt) : IGameEvent;
public sealed record OtherPlayRestrictionGrantedEvent(long ProgramFrameId, long PhaseParentFrameId,
    CardConversionSource Source, string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int PhaseInstanceId, int RecipientSeat, string SkillId, string GrantSourceId, bool DirectedDistanceOne,
    int CostCardId, long CostSequenceBefore, long CostSequenceAfter) : IGameEvent;
public sealed record StartingEquipmentControlCompletedEvent(long ProgramFrameId, long PhaseParentFrameId,
    CardConversionSource Source, int ActualTurnNumber, int ActualTurnOwnerSeat, int CardId,
    CardLocation From, CardLocation To, bool ArmorAcquired, long SequenceBefore, long SequenceAfter) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PhaseStartCardControlReceipt? PhaseStartCardControl { get; init; }
}
