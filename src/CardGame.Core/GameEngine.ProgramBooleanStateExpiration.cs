namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record ProgramBooleanStateLease(CardUseEffectSource Source, string StateId,
        int TurnNumber, int TurnSeat, SkillProgramDamageModifierExpiration Expiration);
    private readonly List<ProgramBooleanStateLease> _programBooleanStateLeases = [];

    private void ScheduleProgramBooleanStateExpiration(ProgramSkillFrame frame, IReadOnlyList<string> stateIds,
        SkillProgramDamageModifierExpiration expiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (!Enum.IsDefined(expiration)) throw new InvalidOperationException("Unknown boolean state expiration.");
        var source = CreateProgramTurnEffectSource(frame);
        foreach (var stateId in stateIds.Distinct(StringComparer.Ordinal))
        {
            _ = GetProgramBooleanStateDefinition(frame, stateId);
            _programBooleanStateLeases.RemoveAll(item => item.Source.OwnerSeat == source.OwnerSeat &&
                item.Source.SkillId == source.SkillId && item.Source.SkillInstanceId == source.SkillInstanceId &&
                item.StateId == stateId);
            _programBooleanStateLeases.Add(new(source, stateId, _turnNumber, _currentSeat, expiration));
        }
    }

    private void ExpireProgramBooleanStateLeases(int turnNumber, int turnSeat) =>
        ExpireProgramBooleanStateLeases(item => item.Expiration == SkillProgramDamageModifierExpiration.CurrentTurnEnd &&
            item.TurnNumber == turnNumber && item.TurnSeat == turnSeat);

    // BeginTurn invokes this after incrementing the actual turn number, before a face-down skip.
    // Inserted phases do not call BeginTurn; extra turns do, even when the owner remains the same.
    private void ExpireProgramBooleanStateLeasesAtOwnerTurnStart(int ownerSeat, bool ownerDied = false) =>
        ExpireProgramBooleanStateLeases(item => item.Source.OwnerSeat == ownerSeat &&
            (ownerDied || item.Expiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart && item.TurnNumber < _turnNumber));

    private void ExpireProgramBooleanStateLeases(Func<ProgramBooleanStateLease, bool> expires)
    {
        var expired = _programBooleanStateLeases.Where(expires).ToArray();
        _programBooleanStateLeases.RemoveAll(item => expires(item));
        foreach (var item in expired)
            SetProgramBooleanState(item.Source.OwnerSeat, item.Source.SkillId, item.Source.SkillInstanceId, item.StateId, false);
    }
}
