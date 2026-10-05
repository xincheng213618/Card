using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryLiangXingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-liang-xing", definition => definition);
        builder.AddGeneral(new("ol:liang-xing", "梁兴", "ol-liang-xing", "ol:luelve", "qun", 4, ["ol:zhuanxi"], GeneralGender.Male)
        { CharacterId = "character:liang-xing", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
