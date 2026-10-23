using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Lan;

public static class LanContent
{
    public static IReadOnlyList<LanMode> Modes { get; } = Array.AsReadOnly(new[]
    {
        new LanMode("identity:lan-5", "五人标准身份", 5),
        new LanMode("identity:lan-8", "八人标准身份", 8)
    });
    private static readonly Lazy<ContentRegistry> Registry = new(() =>
    {
        var standard = StandardContentRegistry.Create();
        return ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new LanPackage(standard));
    });
    public static ContentRegistry Content => Registry.Value;

    private sealed class LanPackage(ContentRegistry standard) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("lan-prototype", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 0, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var mode in Modes)
                builder.AddMode(standard.Modes[$"identity:standard-{mode.PlayerCount}"] with
                { Id = mode.Id, Name = mode.Name, DeckId = "classic:standard-108" });
        }
    }
}
