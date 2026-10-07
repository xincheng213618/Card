using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryYanJunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-yan-jun");
        builder.AddGeneral(new("ol:yan-jun", "严畯", "ol-yan-jun", "ol:guanchao", "wu", 3,
            ["ol:xunxian"], GeneralGender.Male)
        { CharacterId = "character:yan-jun", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
