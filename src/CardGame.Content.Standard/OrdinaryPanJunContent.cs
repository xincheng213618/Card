using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryPanJunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-pan-jun", definition => definition);
        builder.AddGeneral(new("ol:pan-jun", "潘濬", "ol-pan-jun", "ol:guanwei", "wu", 3,
            ["ol:gongqing"], GeneralGender.Male)
        { CharacterId = "character:pan-jun", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
