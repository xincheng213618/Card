using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryLiuXieContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-liu-xie", definition => definition);
        builder.AddGeneral(new("ol:liu-xie", "刘协", "ol-liu-xie", "ol:tianming", "qun", 3, ["ol:mizhao"], GeneralGender.Male)
        { CharacterId = "character:liu-xie", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
