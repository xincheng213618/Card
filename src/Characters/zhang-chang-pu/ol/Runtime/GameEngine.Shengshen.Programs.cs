namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 省身: draw one, or two when the owner's hand is the fewest alive; then
    // arm the next Yanjiao reveal by one, or two when the owner's HP is the
    // fewest alive, capped at four.
    private void ShenShenDrawAndArmBonus(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId)) return;
        var alive = _players.Where(player => player.IsAlive).ToArray();
        var minimumHand = alive.Min(player => GetHand(player).Count);
        DrawCards(owner, GetHand(owner).Count == minimumHand ? 2 : 1, true,
            new($"skill-program.{active.SkillId}.shengshen-draw"));
        var minimumHp = alive.Min(player => Math.Max(0, player.Hp));
        var marker = effect.Marker!.Value;
        var current = owner.Markers.GetValueOrDefault(marker);
        var armed = Math.Min(4, current + (Math.Max(0, owner.Hp) == minimumHp ? 2 : 1));
        if (armed != current)
            MutateParticipantMarker(GetActiveProgramFrame(active.Id), active.OwnerSeat, marker, armed - current);
    }
}
