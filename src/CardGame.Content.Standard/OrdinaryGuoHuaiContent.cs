using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryGuoHuaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-guo-huai", definition => definition);
        builder.AddGeneral(new("ol:guo-huai", "郭槐", "ol-guo-huai", "ol:zhefu", "jin", 3,
            ["ol:yidu"], GeneralGender.Female)
        { CharacterId = "character:guo-huai-female", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
