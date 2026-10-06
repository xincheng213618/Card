using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhangChangPuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-zhang-chang-pu", definition => definition);
        builder.AddGeneral(new("ol:zhang-chang-pu", "张昌蒲", "ol-zhang-chang-pu", "ol:yanjiao", "wei", 3,
            ["ol:shengshen"], GeneralGender.Female)
        { CharacterId = "character:zhang-chang-pu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
