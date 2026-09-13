using CardGame.Wpf.Persistence;

internal sealed class MemorySaveStore : IGameSaveStore
{
    private readonly Dictionary<GameSaveSlot, GameSaveFile> _saves = [];
    public int WriteCount { get; private set; }
    public DateTimeOffset? GetSavedAt(GameSaveSlot slot) => _saves.GetValueOrDefault(slot)?.SavedAtUtc;
    public GameSaveFile Read(GameSaveSlot slot) => _saves[slot];
    public void Write(GameSaveSlot slot, GameSaveFile save)
    {
        _saves[slot] = save;
        WriteCount++;
    }
}
