using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryTengFangLanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-teng-fang-lan");
        builder.AddGeneral(new("ol:teng-fang-lan", "滕芳兰", "ol-teng-fang-lan", "ol:luochong", "wu", 3,
            ["ol:aichen"], GeneralGender.Female)
        { CharacterId = "character:teng-fang-lan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
