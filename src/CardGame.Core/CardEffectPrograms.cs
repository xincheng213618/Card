namespace CardGame.Core;

public sealed record DelayedEffectContinuation(int CardId, CardKind EffectiveKind, int OwnerSeat,
    int TurnNumber, TurnPhase Phase, long ParentFrameId, ResolutionFrameKind? ParentKind, ResolutionFrameStep? ParentStep);
public sealed record CardEffectBeforeApplyFrame(long Id, long ParentFrameId, CardActionContext Action,
    IReadOnlyList<int> FinalDesignatedTargetSeats, IReadOnlyList<ProgramCardTriggerCandidate> Candidates,
    NullificationWindowFrame? OrdinaryReturn = null, DelayedEffectContinuation? DelayedReturn = null,
    int CandidateIndex = 0, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id,ResolutionFrameKind.CardEffectBeforeApply,Step);
