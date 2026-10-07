using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryDingFengContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-ding-feng");
        // The official API omits sex; Male is the historical/editorial identity.
        builder.AddGeneral(new("ol:ding-feng", "丁奉", "ol-ding-feng", "ol:duanbing", "wu", 4,
            ["ol:fenxun"], GeneralGender.Male)
        { CharacterId = "character:ding-feng", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
