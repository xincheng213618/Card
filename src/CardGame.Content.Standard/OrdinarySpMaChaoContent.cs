using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySpMaChaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-ma-chao", definition => definition,
            new Dictionary<string, SkillTag> { ["ol:zhuiji"] = SkillTag.Locked });
        builder.AddGeneral(new("ol:sp-ma-chao", "SP马超", "ol-sp-ma-chao", "ol:zhuiji", "qun", 4,
            ["ol:shichou"], GeneralGender.Male)
        { CharacterId = "character:ma-chao", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
