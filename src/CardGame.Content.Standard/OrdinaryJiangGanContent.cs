using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryJiangGanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-jiang-gan", definition => definition);
        builder.AddGeneral(new("ol:jiang-gan", "蒋干", "ol-jiang-gan", "ol:weicheng", "wei", 3,
            ["ol:daoshu"], GeneralGender.Male)
        { CharacterId = "character:jiang-gan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
