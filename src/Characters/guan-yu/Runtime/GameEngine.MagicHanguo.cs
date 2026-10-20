namespace CardGame.Core;

public sealed partial class GameEngine
{
    private DemonRoundFocusEvent? DemonFocus(int target) => CompleteProgramEventHistory().OfType<DemonRoundFocusEvent>()
        .LastOrDefault(e => e.TargetSeat == target && e.RoundNumber == _roundNumber);
    private FactionResponsePolicySource? GetDemonDodgeRequest(CharacterState owner, CardKind kind) =>
        kind == CardKind.Dodge && DemonFocus(owner.Seat) is { } focus && GetFactionProviderSeats(owner.Seat, "magic-any").Count > 0
            ? new(focus.Source.SkillId, focus.Source.SkillInstanceId, "round-aid", "magic-any", kind, 0, 0) : null;

    private void ReturnDemonRoundHolds()
    {
        foreach (var player in _players)
        {
            var held = _cardZones.CardsAt(CardLocation.RoundHold(player.Seat)).ToArray();
            if (held.Length > 0) MoveCards(held, CardLocation.RoundHold(player.Seat), player.IsAlive ? CardLocation.Hand(player.Seat) : CardLocation.DiscardPile, new("skill-program.hanguo.return"));
        }
    }

}
