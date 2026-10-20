namespace CardGame.Core;

public sealed record WushuangTactic(int Id, string Name, string Description, int Cost, int Quality, string? SkillId = null)
{
    public bool IsImplemented { get; init; }
    public IReadOnlyList<int> RelatedIds { get; init; } = Array.AsReadOnly(Array.Empty<int>());
}

public static class WushuangTactics
{
    public static IReadOnlyList<WushuangTactic> All => WushuangCatalogue.Tactics;
    public static IReadOnlyDictionary<int, WushuangTactic> ById { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<int, WushuangTactic>(All.ToDictionary(t => t.Id));
}
