using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryXunChenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-xun-chen", definition => definition);
        // Male is inferred from the official biography's 颍川名士; API supplies no gender field.
        builder.AddGeneral(new("ol:xun-chen", "荀谌", "ol-xun-chen", "ol:fenglve", "qun", 3, ["ol:moushi"], GeneralGender.Male)
        { CharacterId = "character:xun-chen", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
