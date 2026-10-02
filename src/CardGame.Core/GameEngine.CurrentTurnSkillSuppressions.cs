namespace CardGame.Core;

// Issued facts outlive their source. They qualify every present or future grant,
// rather than changing local grant enablement or retaining a restore list.
public sealed record CurrentTurnNonLockedSkillSuppression(int TurnNumber, int TurnOwnerSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, int TargetSeat);
public sealed record CurrentTurnNonLockedSkillSuppressionIssuedEvent(
    CurrentTurnNonLockedSkillSuppression Suppression) : IGameEvent;
public sealed record CurrentTurnNonLockedSkillSuppressionsExpiredEvent(
    int TurnNumber, int TurnOwnerSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<CurrentTurnNonLockedSkillSuppression> _currentTurnSkillSuppressions = [];
    private long _currentTurnSkillSuppressionRevision;
    private long _currentTurnQualificationStamp;
    private (long Projection, long Suppression) _currentTurnQualificationDependencies;

    private void IssueCurrentTurnNonLockedSkillSuppression(ProgramSkillFrame frame, int targetSeat)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _turnProgression.OwnerSeat != _currentSeat || _turnProgression.TurnNumber != _turnNumber ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("Suppression requires its actual owned program frame and living other target.");
        var fact = new CurrentTurnNonLockedSkillSuppression(_turnNumber, _currentSeat,
            frame.Id, frame.InstructionIndex - 1, CreateProgramTurnEffectSource(frame), targetSeat);
        var existing = _currentTurnSkillSuppressions.SingleOrDefault(s =>
            s.ParentFrameId == fact.ParentFrameId && s.EffectIndex == fact.EffectIndex && s.TargetSeat == targetSeat);
        if (existing is not null)
        {
            if (existing != fact) throw new InvalidOperationException("Suppression issue identity changed meaning.");
            return;
        }
        _currentTurnSkillSuppressions.Add(fact);
        _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnNonLockedSkillSuppressionIssuedEvent(fact));
    }

    private bool IsCurrentTurnSkillGrantQualified(CharacterState owner, SkillGrant grant) =>
        !_currentTurnSkillSuppressions.Any(s => s.TurnNumber == _turnNumber &&
            s.TurnOwnerSeat == _currentSeat && s.TargetSeat == owner.Seat) ||
        _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked);

    private long CaptureCurrentTurnQualificationStamp()
    {
        var projection = CaptureCombinedProjectionDependencyStamp();
        if (_currentTurnSkillSuppressionRevision == 0) return projection;
        var current = (projection, _currentTurnSkillSuppressionRevision);
        if (current != _currentTurnQualificationDependencies)
        {
            _currentTurnQualificationDependencies = current;
            _currentTurnQualificationStamp++;
        }
        // Disjoint from the nonnegative legacy projection stamp.
        return -_currentTurnQualificationStamp;
    }

    private void ExpireCurrentTurnNonLockedSkillSuppressions(int turnNumber, int turnOwnerSeat)
    {
        if (_currentTurnSkillSuppressions.RemoveAll(s => s.TurnNumber == turnNumber &&
            s.TurnOwnerSeat == turnOwnerSeat) == 0) return;
        _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnNonLockedSkillSuppressionsExpiredEvent(turnNumber, turnOwnerSeat));
    }
}
