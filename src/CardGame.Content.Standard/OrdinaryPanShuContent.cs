using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryPanShuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-pan-shu", definition => definition);
        builder.AddGeneral(new("ol:pan-shu", "潘淑", "ol-pan-shu", "ol:zhiren", "wu", 3,
            ["ol:yaner"], GeneralGender.Female)
        { CharacterId = "character:pan-shu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
