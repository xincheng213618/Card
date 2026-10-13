using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramCardDeclarationDraft(string StateId, string Bind, int CardId, int Tier,
    int? DeclarerSeat = null)
{
    private IReadOnlyList<int> _nearestSeats = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> NearestSeats
    { get => _nearestSeats; init => _nearestSeats = Array.AsReadOnly(value.ToArray()); }
}

public sealed record ProgramParticipantTopView(int Seat)
{
    private IReadOnlyList<int> _cardIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> CardIds
    { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed record ProgramStrategicDraft(SkillProgramEffectOp Op, int TargetSeat, string? Bind, int Maximum)
{
    private IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Candidates
    { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected
    { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCardDeclarationDraft? CardDeclarationDraft { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramParticipantTopView? ParticipantTopView { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramStrategicDraft? StrategicDraft { get; init; }
}
