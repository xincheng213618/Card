using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryWangShuangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-wang-shuang");
        builder.AddGeneral(new("ol:wang-shuang", "王双", "ol-wang-shuang", "ol:zhui-lie", "wei", 8,
            [], GeneralGender.Male)
        { CharacterId = "character:wang-shuang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
