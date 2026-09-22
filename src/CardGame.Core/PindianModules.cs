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

/// <summary>Only revealed contest facts and currently claimable cards, never either private hand.</summary>
public sealed record PindianResultContext(
    int OwnerSeat, string InitiatingSkillId, PindianResult Result, IReadOnlyList<int> AvailableCardIds);

public interface IPindianResultModule
{
    string SkillId { get; }
    int Revision { get; }
    PindianCardClaimPlan? CreatePlan(PindianResultContext context);
}

/// <summary>Optional acquisition of exactly one revealed card still owned by this contest.</summary>
public sealed record PindianCardClaimPlan(
    SkillPromptPresentation Presentation, string Prompt, int CardId, bool AiPrefersActivation = true);

/// <summary>Ask the activating player for one hand card and another player with hand cards.</summary>
public sealed record BeginSkillPindian : SkillModuleEffect;

public enum PindianStep { ChooseParticipants, ChooseOpponentCard, AfterResult }
public sealed record PindianTriggerCandidate(int OwnerSeat, string SkillId);

/// <summary>Trusted-host data only. It is not included in player snapshots.</summary>
public sealed record PindianFrame(
    long Id, long ParentFrameId, string SkillId, SkillPromptPresentation Presentation,
    int SourceSeat, int? OpponentSeat = null, int? SourceCardId = null,
    SkillKind? LegacySkill = null, PindianStep PindianStep = PindianStep.ChooseParticipants,
    PindianResult? Result = null, IReadOnlyList<PindianTriggerCandidate>? Candidates = null,
    int CandidateIndex = 0, PindianCardClaimPlan? ClaimPlan = null,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.Pindian, Step);

public sealed record PindianResultDeterminedEvent(
    long FrameId, string SkillId, PindianResult Result) : IGameEvent;
public sealed record PindianCardClaimedEvent(
    long FrameId, string SkillId, int OwnerSeat, int CardId) : IGameEvent;
