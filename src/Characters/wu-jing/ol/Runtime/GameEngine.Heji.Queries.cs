namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WuJingHejiIncoming(CardUseFrame use) => use.Action is { Type: CardActionType.Use } a && use.TargetSeats is [var target] &&
        a.EffectiveDesignatedTargetSeats.SequenceEqual([target]) && _players[target].IsAlive &&
        (use.CardKind == CardKind.Duel || IsSlashCard(use.CardKind) && a.EffectiveIsRed == true);
}
