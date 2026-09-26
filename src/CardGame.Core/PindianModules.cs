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

public enum PindianStep { ChooseParticipants, ChooseSourceCard, ChooseOpponentCard }

/// <summary>Trusted-host data only. It is not included in player snapshots.</summary>
public sealed record PindianFrame(
    long Id, long ParentFrameId, string SkillId, SkillPromptPresentation Presentation,
    int SourceSeat, int? OpponentSeat = null, int? SourceCardId = null,
    PindianStep PindianStep = PindianStep.ChooseParticipants,
    PindianResult? Result = null,
    string? ProgramResultBind = null,
    SkillProgramCardSetVisibility ProgramResultVisibility = SkillProgramCardSetVisibility.Public,
    IReadOnlyList<int>? ParentProcessingCardIds = null,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.Pindian, Step);

public sealed record PindianResultDeterminedEvent(
    long FrameId, string SkillId, PindianResult Result) : IGameEvent;
