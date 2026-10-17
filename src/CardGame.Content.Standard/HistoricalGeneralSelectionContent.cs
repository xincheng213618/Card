using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Content.Standard;

// Old fingerprints include these AI hints. Recreate the original definitions
// for recorded policies instead of rewriting a checkpoint's content hash.
internal static class HistoricalGeneralSelectionContent
{
    internal static IGameContentPackage Wrap(IGameContentPackage package) => new HistoricalPackage(package, true);
    internal static IGameContentPackage RestoreSelectionPools(IGameContentPackage package) => new HistoricalPackage(package, false);

    private static IReadOnlyDictionary<Role, double>? Weights(string id) => id switch
    {
        "classic:ganglie" or "standard:ganglie" => Values(39, 39, 35, 35),
        "classic:guicai" or "standard:guicai" => Values(41, 41, 36, 36),
        "classic:kongcheng" or "standard:kongcheng" => Values(25, 34, 25, 25),
        "classic:qicai" or "standard:qicai" => Values(33, 33, 37, 37),
        "standard:yuanhu" => Values(38, 38, 33, 33),
        _ => null
    };

    private static IReadOnlyDictionary<Role, double> Values(double lord, double loyalist, double rebel, double renegade) =>
        new ReadOnlyDictionary<Role, double>(new Dictionary<Role, double>
        { [Role.Lord] = lord, [Role.Loyalist] = loyalist, [Role.Rebel] = rebel, [Role.Renegade] = renegade });

    private sealed class HistoricalPackage(IGameContentPackage package, bool restoreWeights) : IGameContentPackage
    {
        public PackageManifest Manifest => package.Manifest;
        public void Register(IContentRegistryBuilder builder) => package.Register(new HistoricalBuilder(builder, restoreWeights));
    }

    private sealed class HistoricalBuilder(IContentRegistryBuilder builder, bool restoreWeights) : IContentRegistryBuilder
    {
        public void AddCard(ContentCardDefinition definition) => builder.AddCard(definition);
        public void AddSkill(ContentSkillDefinition definition) => builder.AddSkill(restoreWeights && Weights(definition.Id) is { } weights
            ? definition with { SelectionWeights = weights } : definition);
        public void AddGeneral(ContentGeneralDefinition definition) => builder.AddGeneral(definition);
        public void AddDeck(ContentDeckRecipe definition) => builder.AddDeck(definition);
        public void AddMode(ContentModeDefinition definition) => builder.AddMode(definition with { GeneralSelectionPoolIds = null });
    }
}
