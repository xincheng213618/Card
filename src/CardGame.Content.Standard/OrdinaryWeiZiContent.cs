using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryWeiZiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-wei-zi", definition => definition);
        builder.AddGeneral(new("ol:wei-zi", "卫兹", "ol-wei-zi", "ol:yuanzi", "qun", 3,
            ["ol:liejie"], GeneralGender.Male)
        { CharacterId = "character:wei-zi", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
