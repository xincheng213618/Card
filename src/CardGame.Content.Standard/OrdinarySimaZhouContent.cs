using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySimaZhouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sima-zhou");
        builder.AddGeneral(new("ol:sima-zhou", "司马伷", "ol-sima-zhou", "ol:caiwang", "jin", 4,
            ["ol:najiang"], GeneralGender.Male)
        { CharacterId = "character:sima-zhou", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
