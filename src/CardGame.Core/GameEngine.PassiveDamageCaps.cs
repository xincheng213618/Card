namespace CardGame.Core;

public sealed partial class GameEngine
{
    private (CardUseEffectSource Source, int Cap)? GetPassiveProgramDamageCap(IDamageAttempt attack)
    {
        if (!IsValidPlayerSeat(attack.TargetSeat) || !_players[attack.TargetSeat].IsAlive) return null;
        var sourceSeat = !attack.IsSourceLess && IsValidPlayerSeat(attack.SourceSeat)
            ? attack.SourceSeat : (int?)null;
        var programCap = FindPassiveProgramDamageCap(_players[attack.TargetSeat], sourceSeat);
        var activityCap = JiangeDamageCap(attack);
        return activityCap is { } activity && (programCap is null || activity.Cap <= programCap.Value.Cap)
            ? activity : programCap;
    }

    // Only active bindings participate. A fixed target cap needs no damage
    // source; source-dependent caps require a real living source.
    private (CardUseEffectSource Source, int Cap)? FindPassiveProgramDamageCap(CharacterState target,
        int? sourceSeat, IReadOnlySet<string>? visibleSkillIds = null)
    {
        if (!target.IsAlive) return null;
        (CardUseEffectSource Source, int Cap)? result = null;
        foreach (var instance in GetSkillBindingShard(target).ProgramInstances)
        {
            if (visibleSkillIds is not null && !visibleSkillIds.Contains(instance.SkillId)) continue;
            foreach (var modifier in instance.Program.DamageModifiers)
            {
                if (!modifier.CapsToAmount || modifier.SourceScope != SkillProgramDamageModifierSourceScope.DamageParticipant ||
                    modifier.CardKinds.Count != 0) continue;
                var eligible = modifier.Condition switch
                    {
                        SkillProgramDamageModifierCondition.Always => true,
                        SkillProgramDamageModifierCondition.DamageSourceAttackRangeBelowThree =>
                            sourceSeat is { } seat && IsValidPlayerSeat(seat) && _players[seat].IsAlive && GetAttackRange(seat) < 3,
                        _ => false
                    };
                if (!eligible) continue;
                if (result is null || modifier.Amount < result.Value.Cap)
                    result = (new CardUseEffectSource(instance.SkillId, modifier.Id, target.Seat, instance.SkillInstanceId), modifier.Amount);
            }
        }
        return result;
    }

    private int? GetIncomingFixedDamageCap(CharacterState target, IReadOnlySet<string>? visibleSkillIds = null) =>
        FindPassiveProgramDamageCap(target, null, visibleSkillIds)?.Cap;

    private bool HasNextRecipientPassiveDamageCap(IDamageAttempt attack)
    {
        var state = attack is ProgramAttackHandle
            ? (GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats, GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex)
            : (GetCardAttackState(attack.ResolutionId).ChainedTargetSeats, GetCardAttackState(attack.ResolutionId).ChainedTargetIndex);
        var next = (state.ChainedTargetSeats ?? []).Skip(state.ChainedTargetIndex)
            .FirstOrDefault(seat => IsValidPlayerSeat(seat) && _players[seat].IsAlive, -1);
        return next >= 0 && GetIncomingFixedDamageCap(_players[next]) is not null;
    }
}
