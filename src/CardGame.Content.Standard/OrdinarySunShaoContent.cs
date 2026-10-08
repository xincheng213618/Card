using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySunShaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sun-shao");
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:sun-shao", "孙邵", "ol-sun-shao", "ol:bizheng", "wu", 3,
            ["ol:yidian"], GeneralGender.Male)
        { CharacterId = "character:sun-shao", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
