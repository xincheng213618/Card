namespace CardGame.Core;
public sealed partial class GameEngine
{
    private string? WuHfEntityMark(int cardId) => HasWuHuangFuSongCapability && WuHfTurnOpen(WuHfTurn()) &&
        ProgramEventHistory<WuHuangFuSongLianjieEntityEvent>().Any(e => e.CardId == cardId && e.Turn == WuHfTurn()) ? "连捷" : null;

}
