using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryQingHeGongZhuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-qinghe-gongzhu");
        builder.AddGeneral(new("ol:qinghe-gongzhu", "清河公主", "ol-qinghe-gongzhu", "ol:changji", "wei", 3,
            ["ol:zengou"], GeneralGender.Female)
        { CharacterId = "character:qinghe-gongzhu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
