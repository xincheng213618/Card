using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouZhitianCount(int seat) => _mouZhitianCounts.GetValueOrDefault(seat, 7);
    private IReadOnlyList<Card> MouZhitianTop(int seat) => _cardZones.CardsAt(CardLocation.DrawPile)
        .TakeLast(MouZhitianCount(seat)).Reverse().ToArray();
    private IEnumerable<CardSnapshot> MouZhitianPrivateCards(int viewerSeat) =>
        IsValidPlayerSeat(viewerSeat) && _players[viewerSeat].IsAlive &&
        HasMouOperation(_players[viewerSeat], SkillProgramEffectOp.MouZhitianView) &&
        (_phase == TurnPhase.Play && _currentSeat == viewerSeat ||
         ActiveFireAttack is { SourceSeat: var source, FireAttackSelection.RevealedCardId: not null } && source == viewerSeat)
        ? MouZhitianTop(viewerSeat).Select(ToSnapshot) : [];
}
