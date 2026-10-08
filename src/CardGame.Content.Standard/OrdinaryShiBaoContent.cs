using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryShiBaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-shi-bao");
        builder.AddGeneral(new("ol:shi-bao", "石苞", "ol-shi-bao", "ol:zhuosheng", "jin", 4,
            [], GeneralGender.Male)
        { CharacterId = "character:shi-bao", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
