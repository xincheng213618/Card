using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhouFangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-zhou-fang", definition => definition);
        builder.AddGeneral(new("ol:zhou-fang", "周鲂", "ol-zhou-fang", "ol:duanfa", "wu", 3,
            ["ol:youdi"], GeneralGender.Male)
        { CharacterId = "character:zhou-fang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
