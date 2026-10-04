using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySpCaiWenJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-cai-wen-ji", definition => definition);
        builder.AddGeneral(new("ol:sp-cai-wen-ji", "SP蔡文姬", "ol-sp-cai-wen-ji", "ol:chenqing", "wei", 3,
            ["ol:moshi"], GeneralGender.Female)
        { CharacterId = "character:cai-wen-ji", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
