using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryZhangHuaContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhang-hua");
        builder.AddGeneral(new("ol:zhang-hua", "张华", "ol-zhang-hua", "ol:bihun", "jin", 3,
            ["ol:jianhe", "ol:chuanwu"], GeneralGender.Male)
        { CharacterId = "character:zhang-hua", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
