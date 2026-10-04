using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record ProgramGameTargetHandHpReceipt
{
    private IReadOnlyList<int> _cardIds = Array.Empty<int>();
    private IReadOnlyList<CardLocation> _locations = Array.Empty<CardLocation>();
    [JsonConstructor]
    public ProgramGameTargetHandHpReceipt(int instructionIndex, string stateId, int targetSeat, int handCount,
        int hp, bool draw, int requiredCount, string stage, IReadOnlyList<int> cardIds,
        IReadOnlyList<CardLocation> locations, long sequenceBefore = 0, long sequenceAfter = 0, int actualCount = 0)
    {
        InstructionIndex = instructionIndex; StateId = stateId; TargetSeat = targetSeat; HandCount = handCount;
        Hp = hp; Draw = draw; RequiredCount = requiredCount; Stage = stage; CardIds = cardIds; Locations = locations;
        SequenceBefore = sequenceBefore; SequenceAfter = sequenceAfter; ActualCount = actualCount;
    }
    public int InstructionIndex { get; init; }
    public string StateId { get; init; }
    public int TargetSeat { get; init; }
    public int HandCount { get; init; }
    public int Hp { get; init; }
    public bool Draw { get; init; }
    public int RequiredCount { get; init; }
    public string Stage { get; init; }
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<CardLocation> Locations { get => _locations; init => _locations = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public int ActualCount { get; init; }
}
public sealed record GameTargetHandHpChoiceIssuedEvent(long FrameId, int OwnerSeat, string SkillId, string StateId,
    int TargetSeat, int HandCount, int Hp, bool Draw) : IGameEvent;
public sealed record GameTargetHandHpMovementIssuedEvent(long FrameId, int TargetSeat, bool Draw, int Count, long Before, long After) : IGameEvent;
