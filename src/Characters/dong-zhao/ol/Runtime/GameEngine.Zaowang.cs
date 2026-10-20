using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ArmZaowang(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SelectedTargetSeats is not [var seat] || !_players[seat].IsAlive) throw new InvalidOperationException("Zaowang needs one living recipient.");
        var target = _players[seat];
        if (f.ZaowangPaidTargetSeat is not null) throw new InvalidOperationException("Zaowang cannot pay twice.");
        ReplaceRuntimeTop(f = f with { ZaowangPaidTargetSeat = seat });
        if (_modeDefinition.ModeKind == ContentModeKind.Identity)
            target.AddZaowangGrant(new(f.Id, f.OwnerSeat, seat, target.Role, f.SkillInstanceId, f.GameplayHash));
        target.MaxHp = checked(target.MaxHp + 1);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, 1, target.MaxHp, f.SkillId));
        AdvanceEventRulesAndQueueFact(new ZaowangGrantedEvent(f.Id, f.OwnerSeat, seat, f.SkillId, f.SkillInstanceId, f.GameplayHash, target.Hp, target.MaxHp));
        return SkillProgramStepOutcome.Continue;
    }
    private void ApplyZaowangSuccession(long deathId, CharacterState victim)
    {
        if (_modeDefinition.ModeKind != ContentModeKind.Identity || victim.Role != Role.Lord) return;
        // Multiple eligible heirs resolve in seat order, the normal trigger order.
        var successor = _players.Where(p => p.IsAlive && p.Role == Role.Loyalist && p.ZaowangGrants.Any(g => g.OriginalRole == Role.Loyalist))
            .OrderBy(p => p.Seat).FirstOrDefault();
        if (successor is null) return;
        victim.ExchangeIdentityWith(successor); successor.RoleRevealed = true;
        AdvanceEventRulesAndQueueFact(new IdentityExchangedEvent(deathId, victim.Seat, successor.Seat));
        AdvanceEventRulesAndQueueFact(new RoleRevealedEvent(victim.Seat, victim.Role));
        AdvanceEventRulesAndQueueFact(new RoleRevealedEvent(successor.Seat, successor.Role));
    }
    private bool ZaowangRebelDeathWins(CharacterState victim, CharacterState? killer) =>
        _modeDefinition.ModeKind == ContentModeKind.Identity && victim.Role == Role.Rebel &&
        victim.ZaowangGrants.Any(g => g.OriginalRole == Role.Rebel) && killer is { Role: Role.Lord or Role.Loyalist };
}
