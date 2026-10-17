using CardGame.Core;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static readonly IReadOnlyList<string> LegacyPlayerGeneralIds = Array.AsReadOnly(new[]
    {
        "classic:guan-yu", "classic:zhao-yun", "classic:zhang-fei", "classic:ma-chao",
        "classic:sun-quan", "classic:cao-cao", "classic:huang-yueying", "classic:zhou-yu"
    });
    private static readonly Lazy<ContentRegistry> PlayerContent = new(() => ComposedSkillContentRegistry.CreateShowcase());
    private static readonly Lazy<IReadOnlyList<string>> PlayerGeneralIds = new(CreatePlayerGeneralIds);

    /// <summary>The same showcase content source used by campaign battles, without encounter-only packages.</summary>
    public static ContentRegistry PlayerRosterRegistry => PlayerContent.Value;

    /// <summary>Playable ordinary identity generals; encounter forms, national-war and demonstration pools are excluded.</summary>
    public static IReadOnlyList<string> GetPlayerGeneralIds() => PlayerGeneralIds.Value;

    private static IReadOnlyList<string> CreatePlayerGeneralIds()
    {
        var registry = PlayerRosterRegistry;
        var fanIds = StandardClassicGeneralPackage.FanGeneralIds.ToHashSet(StringComparer.Ordinal);
        // The classic identity pool includes the official classic/OL/LE expansions. The boundary mode
        // also declares its ordinary identity pool; demonstration identity modes deliberately stay out.
        var ordinaryIds = new[] { "identity:classic-8", "identity:classic-boundary-8" }
            .SelectMany(id => registry.Modes[id].GeneralPoolIds ?? []);
        return Array.AsReadOnly(LegacyPlayerGeneralIds.Concat(ordinaryIds)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !fanIds.Contains(id) && registry.IsGeneralPlayable(id)).ToArray());
    }
}
