namespace CardGame.Core;

/// <summary>
/// One typed opportunity returned by a trigger collector. The candidate is
/// data-only so a paused resolution can retain its identity without retaining
/// a skill object, delegate, or UI callback.
/// </summary>
public sealed record DamageTriggerCandidate(
    int OwnerSeat,
    SkillKind Skill,
    string CandidateId,
    int Priority = 0,
    bool IsOptional = false,
    DamageSkillEffectKind Effect = DamageSkillEffectKind.None,
    string? ProgramId = null,
    string? ProgramTriggerId = null,
    string? SkillInstanceId = null,
    string? GameplayHash = null,
    int OccurrenceIndex = 0)
{
    public bool IsProgram => ProgramId is not null;

    public ProgramTriggerCandidate? ToProgramCandidate() =>
        IsProgram
            ? new ProgramTriggerCandidate(
                OwnerSeat,
                ProgramId!,
                ProgramTriggerId!,
                SkillInstanceId!,
                GameplayHash!,
                Priority,
                OccurrenceIndex)
            : null;
}

public sealed record JudgmentTriggerCandidate(
    int OwnerSeat,
    SkillKind Skill,
    string CandidateId,
    int Priority = 0,
    string? ProgramId = null,
    string? ProgramTriggerId = null,
    string? GameplayHash = null)
{
    public bool IsProgram => ProgramId is not null;
}

/// <summary>
/// Stable ordering for damage-trigger candidates. The current slice collects
/// candidates from the current alive owners; built-in skills may still keep a
/// damaged-player-only timing rule. The ordering is deliberately independent
/// of that policy so later multi-owner trigger windows can reuse it.
/// </summary>
public static class DamageTriggerOrdering
{
    public static IReadOnlyList<DamageTriggerCandidate> Order(
        IEnumerable<DamageTriggerCandidate> candidates,
        int currentActorSeat,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateSeat(currentActorSeat, playerCount, nameof(currentActorSeat));

        var materialized = candidates.ToArray();
        if (materialized.Any(candidate =>
                candidate.OwnerSeat < 0 || candidate.OwnerSeat >= playerCount))
        {
            throw new ArgumentException(
                "A damage trigger candidate owner must be a seat in the current match.",
                nameof(candidates));
        }

        if (materialized.Any(candidate => string.IsNullOrWhiteSpace(candidate.CandidateId)))
        {
            throw new ArgumentException(
                "A damage trigger candidate must have a stable candidate id.",
                nameof(candidates));
        }

        if (materialized.Any(candidate =>
                candidate.IsProgram !=
                (candidate.ProgramTriggerId is not null && candidate.SkillInstanceId is not null &&
                 candidate.GameplayHash is not null) ||
                !candidate.IsProgram &&
                (candidate.ProgramTriggerId is not null || candidate.SkillInstanceId is not null ||
                 candidate.GameplayHash is not null) ||
                candidate.IsProgram &&
                (candidate.Skill != SkillKind.None || candidate.Effect != DamageSkillEffectKind.None)))
        {
            throw new ArgumentException(
                "A configured damage candidate must retain its program, trigger, skill instance and gameplay hash.",
                nameof(candidates));
        }

        if (materialized
            .GroupBy(candidate =>
                (candidate.OwnerSeat, candidate.Skill, candidate.CandidateId, candidate.OccurrenceIndex))
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Damage trigger candidate ids must be unique per owner and skill.",
                nameof(candidates));
        }

        return materialized
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => GetRelativeSeatOrder(
                currentActorSeat,
                candidate.OwnerSeat,
                playerCount))
            .ThenBy(candidate => (int)candidate.Skill)
            .ThenBy(candidate => candidate.ProgramId ?? candidate.CandidateId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ProgramTriggerId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns clockwise seat distance from the current actor, with that seat
    /// as zero and the next seat as one. Dead-seat filtering belongs to the
    /// trigger collector, not to this pure ordering function.
    /// </summary>
    public static int GetRelativeSeatOrder(
        int currentActorSeat,
        int candidateSeat,
        int playerCount)
    {
        ValidateSeat(currentActorSeat, playerCount, nameof(currentActorSeat));
        ValidateSeat(candidateSeat, playerCount, nameof(candidateSeat));
        return (candidateSeat - currentActorSeat + playerCount) % playerCount;
    }

    private static void ValidateSeat(int seat, int playerCount, string parameterName)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "Player count must be positive.");
        }

        if (seat < 0 || seat >= playerCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, seat, "Seat is outside the current match.");
        }
    }
}

public static class JudgmentTriggerOrdering
{
    public static IReadOnlyList<JudgmentTriggerCandidate> Order(
        IEnumerable<JudgmentTriggerCandidate> candidates,
        int currentActorSeat,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateSeat(currentActorSeat, playerCount, nameof(currentActorSeat));

        var materialized = candidates.ToArray();
        if (materialized.Any(candidate =>
                candidate.OwnerSeat < 0 || candidate.OwnerSeat >= playerCount))
        {
            throw new ArgumentException(
                "A judgment trigger candidate owner must be a seat in the current match.",
                nameof(candidates));
        }

        if (materialized.Any(candidate => string.IsNullOrWhiteSpace(candidate.CandidateId)))
        {
            throw new ArgumentException(
                "A judgment trigger candidate must have a stable candidate id.",
                nameof(candidates));
        }

        if (materialized.Any(candidate =>
                candidate.IsProgram !=
                (candidate.ProgramTriggerId is not null && candidate.GameplayHash is not null) ||
                !candidate.IsProgram &&
                (candidate.ProgramTriggerId is not null || candidate.GameplayHash is not null) ||
                candidate.IsProgram && candidate.Skill != SkillKind.None))
        {
            throw new ArgumentException(
                "A configured judgment candidate must retain its program, trigger and gameplay hash.",
                nameof(candidates));
        }

        if (materialized
            .GroupBy(candidate => (candidate.OwnerSeat, candidate.Skill, candidate.CandidateId))
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Judgment trigger candidate ids must be unique per owner and skill.",
                nameof(candidates));
        }

        return materialized
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => DamageTriggerOrdering.GetRelativeSeatOrder(
                currentActorSeat,
                candidate.OwnerSeat,
                playerCount))
            .ThenBy(candidate => (int)candidate.Skill)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSeat(int seat, int playerCount, string parameterName)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "Player count must be positive.");
        }

        if (seat < 0 || seat >= playerCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, seat, "Seat is outside the current match.");
        }
    }
}
