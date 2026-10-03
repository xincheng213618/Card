namespace CardGame.Core;

/// <summary>Public facts after both physical cards have been revealed. Ties are not wins.</summary>
public sealed record PindianResult(
    int SourceSeat, int OpponentSeat, int SourceCardId, int OpponentCardId,
    int SourceRank, int OpponentRank)
{
    public bool SourceWon => SourceRank > OpponentRank;
    public bool WonBy(int seat) => seat == SourceSeat ? SourceWon :
        seat == OpponentSeat && OpponentRank > SourceRank;
    public int CardOf(int seat) => seat == SourceSeat ? SourceCardId :
        seat == OpponentSeat ? OpponentCardId : throw new ArgumentOutOfRangeException(nameof(seat));
}

public enum PindianStep { ChooseParticipants, ChooseSourceCard, ChooseOpponentCard, ClaimResult = 780 }

/// <summary>Trusted-host data only. It is not included in player snapshots.</summary>
public sealed record PindianFrame(
    long Id, long ParentFrameId, string SkillId, SkillPromptPresentation Presentation,
    int SourceSeat, int? OpponentSeat = null, int? SourceCardId = null,
    PindianStep PindianStep = PindianStep.ChooseParticipants,
    PindianResult? Result = null,
    string? ProgramResultBind = null,
    SkillProgramCardSetVisibility ProgramResultVisibility = SkillProgramCardSetVisibility.Public,
    IReadOnlyList<int>? ParentProcessingCardIds = null,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse,
    IReadOnlyList<int>? ClaimSeats = null,
    int ClaimIndex = 0)
    : ResolutionFrame(Id, ResolutionFrameKind.Pindian, Step)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SourceUsesDrawPileTop { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PindianRandomSelection? RandomSelection { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PindianPolicyClaims? PolicyClaims { get; init; }
}

public sealed record PindianResultDeterminedEvent(
    long FrameId, string SkillId, PindianResult Result) : IGameEvent;
