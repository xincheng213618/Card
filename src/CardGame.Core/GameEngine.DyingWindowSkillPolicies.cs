namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasBlackTrickTargetPolicy => _contentRegistry.ProgramDependencies
        .HasCardPolicy(SkillProgramCardPolicyKind.ProhibitBlackTrickTarget);

    private bool HasDyingWindowQualificationPolicy => _contentRegistry.ProgramDependencies
        .HasCardPolicy(SkillProgramCardPolicyKind.SuppressOthersNonLockedDuringDying);
    private (long Base, long Frame, int Victim, int Turn, int TurnOwner, long SourceRevision, int SourceHp, bool SourceAlive) _dyingQualificationDependencies;
    private long _dyingQualificationStamp;

    // Dynamic qualification, never local enablement or an issued turn-wide fact.
    // The source and the innermost current victim are exempt before querying the
    // source binding shard, so the policy lookup cannot recursively qualify it.
    private bool IsDyingWindowGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (!HasDyingWindowQualificationPolicy || _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
            ActiveDying is not { } dying || owner.Seat == _currentSeat || owner.Seat == dying.VictimSeat ||
            !_players[_currentSeat].IsAlive)
            return true;
        return !HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.SuppressOthersNonLockedDuringDying);
    }

    // The projection dependency must change when a Dying frame enters, leaves,
    // or changes its innermost victim, even if no grant was modified.
    private long CaptureDyingWindowQualificationStamp(long underlying)
    {
        if (!HasDyingWindowQualificationPolicy) return underlying;
        var dying = ActiveDying;
        var source = _players[_currentSeat];
        var next = (underlying, dying?.FrameId ?? 0L, dying?.VictimSeat ?? -1, _turnNumber, _currentSeat,
            source.SkillGrants.Revision, source.Hp, source.IsAlive);
        if (_dyingQualificationStamp == 0 || next != _dyingQualificationDependencies)
        {
            _dyingQualificationDependencies = next;
            _dyingQualificationStamp++;
        }
        return -_dyingQualificationStamp;
    }

    private bool IsExclusiveTurnPeachUseForbidden(int actorSeat, CardKind kind, CardActionType type) =>
        kind == CardKind.Peach && type == CardActionType.Use && _turnNumber > 0 &&
        actorSeat != _currentSeat && actorSeat != ActiveDying?.VictimSeat && _players[_currentSeat].IsAlive &&
        HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.ExclusiveTurnPeachUse, CardKind.Peach);

    private bool IsBlackTrickTargetProhibited(CharacterState target, CardKind kind, bool? effectiveIsRed) =>
        effectiveIsRed == false &&
        CardUseCategoryCatalog.Get(kind) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick &&
        HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitBlackTrickTarget);

    // Some historical zero-entity Duel paths stored false as a placeholder.
    // New color policies must interpret an absent suit and physical cost as
    // colorless without rewriting that legacy action or its replay invariants.
    private static bool? ActualTargetPolicyColor(CardActionContext action) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DyingWindowSkillPolicies.ActualTargetPolicyColor(action);

}
