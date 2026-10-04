using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryYiJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-yi-ji", definition => definition);
        // Male is inferred from the official biography's 蜀汉太中大夫; API supplies no gender field.
        builder.AddGeneral(new("ol:yi-ji", "伊籍", "ol-yi-ji", "ol:jijie", "shu", 3,
            ["ol:jiyuan"], GeneralGender.Male)
        { CharacterId = "character:yi-ji", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
