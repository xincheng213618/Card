using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryLingCaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-ling-cao");
        // The official 2023-12-29 OL announcement explicitly identifies Male.
        builder.AddGeneral(new("ol:ling-cao", "凌操", "ol-ling-cao", "ol:dujin", "wu", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:ling-cao", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
