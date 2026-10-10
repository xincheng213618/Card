using CardGame.Core;

namespace CardGame.Content.Standard;

// Capture the content declarations while forwarding their original registration unchanged.
// Modern entries provide CharacterId; older classic entries use their historical catalogue key.
internal sealed class GeneralVariantContentBuilder(IContentRegistryBuilder target) : IContentRegistryBuilder
{
    private readonly Dictionary<string, ContentGeneralDefinition> _generals = new(StringComparer.Ordinal);
    public void AddCard(ContentCardDefinition definition) => target.AddCard(definition);
    public void AddSkill(ContentSkillDefinition definition) => target.AddSkill(definition);
    public void AddDeck(ContentDeckRecipe definition) => target.AddDeck(definition);
    public void AddMode(ContentModeDefinition definition) => target.AddMode(definition);
    public void AddGeneral(ContentGeneralDefinition definition)
    {
        target.AddGeneral(definition);
        _generals.Add(definition.Id, definition);
    }

    internal IReadOnlyDictionary<string, IReadOnlyList<string>> CreateGroups(IReadOnlyList<string> pool) =>
        pool.Select(id => _generals[id]).GroupBy(CharacterKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)group.Select(general => general.Id).ToArray(), StringComparer.Ordinal);

    private static string CharacterKey(ContentGeneralDefinition general)
    {
        var catalogueKey = general.Id[(general.Id.IndexOf(':') + 1)..];
        if (general.FactionId == "god" && catalogueKey.StartsWith("shen-", StringComparison.Ordinal))
            catalogueKey = catalogueKey[5..];
        var key = general.CharacterId ?? "character:" + catalogueKey;
        return key switch
        {
            "character:gao-da-yi-hao" => "character:zhao-yun",
            "character:fu-huanghou" => "character:fu-huang-hou",
            "character:wolong-zhuge-liang" => "character:zhuge-liang",
            _ => key
        };
    }
}
