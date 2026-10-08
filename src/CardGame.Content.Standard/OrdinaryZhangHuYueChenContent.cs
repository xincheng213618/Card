using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryZhangHuYueChenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhang-hu-yue-chen");
        // The current official API has no gender field; male follows both historical identities.
        builder.AddGeneral(new("ol:zhang-hu-yue-chen", "张虎乐綝", "ol-zhang-hu-yue-chen", "ol:xijue", "jin", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:zhang-hu-yue-chen", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
