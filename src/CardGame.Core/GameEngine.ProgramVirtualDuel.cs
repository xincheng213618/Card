namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>Resolves a direct duel effect between ordered participants; their eligibility belongs to the activation.</summary>
    private SkillProgramStepOutcome BeginProgramVirtualDuel(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        int sourceSeat;
        int targetSeat;
        if (active.TriggerId is null)
        {
            if (active.SelectedTargetSeats.Count != 2 ||
                active.SelectedTargetSeats[0] == active.SelectedTargetSeats[1])
                throw new InvalidOperationException("A virtual duel needs an activation with two different ordered participants.");
            sourceSeat = active.SelectedTargetSeats[0];
            targetSeat = active.SelectedTargetSeats[1];
        }
        else
        {
            // A play-phase trigger duel takes one selected counterpart; the skill
            // owner is the duel source.
            if (active.WindowContext?.Window is not SkillProgramTriggerWindow.PlayPhaseStarting ||
                active.SelectedTargetSeats is not [var counterpart] || counterpart == active.OwnerSeat)
                throw new InvalidOperationException("A triggered virtual duel needs one play-phase selected counterpart.");
            sourceSeat = active.OwnerSeat;
            targetSeat = counterpart;
        }
        if (active.SelectedTargetSeats.Any(seat => seat < 0 || seat >= _players.Count))
            throw new InvalidOperationException("A virtual duel participant seat is invalid.");
        // Paying a preceding cost can trigger other effects; a deceased participant cannot enter a new duel.
        if (active.SelectedTargetSeats.Any(seat => !_players[seat].IsAlive))
            return SkillProgramStepOutcome.Continue;
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A virtual duel cannot overwrite a pending card resolution.");

        var attack = new CardAttackHandle(this, active.Id, sourceSeat, targetSeat,
            card: null, playedCardKind: CardKind.Duel, programSkillFrameId: active.Id);
        ActiveCardAttack = attack;
        ActiveDuel = new DuelHandle(this, attack);
        BeginDuelResponse(ActiveDuel);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
