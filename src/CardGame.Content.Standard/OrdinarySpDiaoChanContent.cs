using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySpDiaoChanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-diao-chan", definition => definition);
        builder.AddGeneral(new("ol:sp-diao-chan", "SP貂蝉", "ol-sp-diao-chan", "ol:lihun", "qun", 3,
            ["ol:biyue"], GeneralGender.Female)
        { CharacterId = "character:diao-chan", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
