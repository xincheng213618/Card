using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryZhuLingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-zhu-ling");
        builder.AddGeneral(new("ol:zhu-ling", "朱灵", "ol-zhu-ling", "ol:zhanyi", "wei", 4,
            [], GeneralGender.Male)
        { CharacterId = "character:zhu-ling", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
