using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryLiuHongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-liu-hong", definition => definition);
        builder.AddGeneral(new("ol:liu-hong", "刘宏", "ol-liu-hong", "ol:yujue", "qun", 4,
            ["ol:tuxing"], GeneralGender.Male)
        { CharacterId = "character:liu-hong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
