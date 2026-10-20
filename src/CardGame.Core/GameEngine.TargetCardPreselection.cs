namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long TargetHandMovementSequence(int seat) => _cardMovements.LastOrDefault(move =>
        move.From != move.To && (move.From == CardLocation.Hand(seat) || move.To == CardLocation.Hand(seat)))?.Sequence ?? 0;
}
