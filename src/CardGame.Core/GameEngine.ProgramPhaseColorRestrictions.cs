namespace CardGame.Core;

public sealed record ProgramPhaseColorRestriction(string SkillId, string SkillInstanceId,
    int OwnerSeat, int TargetSeat, int TurnNumber, bool IsRed, bool UsedSlash = false);
public sealed record ProgramPhaseColorRestrictionGrantedEvent(ProgramPhaseColorRestriction Restriction) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<ProgramPhaseColorRestriction> _programPhaseColorRestrictions = [];

    private void GrantProgramPlayPhaseColorRestriction(ProgramSkillFrame frame, string sourceBind, int targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var binding = GetProgramCardSet(active, sourceBind);
        if (_phase != TurnPhase.Play || targetSeat != _currentSeat || binding.CardIds.Count != 1 ||
            binding.FrozenRevealedSuit is not { } suit || suit == Suit.None)
            throw new InvalidOperationException("A phase color restriction requires a frozen paid color and the current Play phase.");
        var restriction = new ProgramPhaseColorRestriction(active.SkillId, active.SkillInstanceId,
            active.OwnerSeat, targetSeat, _turnNumber, IsRedSuit(suit));
        _programPhaseColorRestrictions.Add(restriction);
        QueueGameEvent(new ProgramPhaseColorRestrictionGrantedEvent(restriction));
    }

    private bool IsPlayPhasePhysicalCardRestricted(CharacterState player, Card card) =>
        _phase == TurnPhase.Play && card.Suit != Suit.None &&
        _programPhaseColorRestrictions.Any(item => item.TurnNumber == _turnNumber && item.TargetSeat == player.Seat &&
            item.IsRed == IsRedSuit(EffectiveSuit(player, card)));

    private IReadOnlyList<string>? UnfulfilledPlayPhaseColorRestrictions(int ownerSeat)
    {
        var instances = _programPhaseColorRestrictions.Where(item => item.OwnerSeat == ownerSeat &&
            item.TurnNumber == _turnNumber && item.TargetSeat == _currentSeat && !item.UsedSlash)
            .Select(item => item.SkillInstanceId).Distinct(StringComparer.Ordinal).ToArray();
        return instances.Length == 0 ? null : instances;
    }

    private void ObservePlayPhaseColorRestriction(IGameEvent payload)
    {
        if (payload is TurnStartedEvent) _programPhaseColorRestrictions.Clear();
        if (payload is not CardUseDeclaredEvent use || _phase != TurnPhase.Play || !IsSlashCard(use.CardKind)) return;
        for (var index = 0; index < _programPhaseColorRestrictions.Count; index++)
            if (_programPhaseColorRestrictions[index].TargetSeat == use.SourceSeat &&
                _programPhaseColorRestrictions[index].TurnNumber == _turnNumber)
                _programPhaseColorRestrictions[index] = _programPhaseColorRestrictions[index] with { UsedSlash = true };
    }
}
