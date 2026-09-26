namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>Resolves a direct duel effect between ordered participants; their eligibility belongs to the activation.</summary>
    private SkillProgramStepOutcome BeginProgramVirtualDuel(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null || active.SelectedTargetSeats.Count != 2 ||
            active.SelectedTargetSeats[0] == active.SelectedTargetSeats[1])
            throw new InvalidOperationException("A virtual duel needs an activation with two different ordered participants.");
        var sourceSeat = active.SelectedTargetSeats[0];
        var targetSeat = active.SelectedTargetSeats[1];
        if (active.SelectedTargetSeats.Any(seat => seat < 0 || seat >= _players.Count))
            throw new InvalidOperationException("A virtual duel participant seat is invalid.");
        // Paying a preceding cost can trigger other effects; a deceased participant cannot enter a new duel.
        if (active.SelectedTargetSeats.Any(seat => !_players[seat].IsAlive))
            return SkillProgramStepOutcome.Continue;
        if (_pendingAttack is not null || _pendingDuel is not null)
            throw new InvalidOperationException("A virtual duel cannot overwrite a pending card resolution.");

        var attack = new AttackResolution(active.Id, sourceSeat, targetSeat,
            card: null, playedCardKind: CardKind.Duel, programSkillFrameId: active.Id);
        _pendingAttack = attack;
        _pendingDuel = new DuelResolution(attack);
        BeginDuelResponse(_pendingDuel);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
