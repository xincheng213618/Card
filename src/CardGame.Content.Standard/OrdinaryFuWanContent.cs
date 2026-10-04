using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryFuWanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-fu-wan", definition => definition);
        builder.AddGeneral(new("ol:fu-wan", "伏完", "ol-fu-wan", "ol:moukui", "qun", 4, [], GeneralGender.Male)
        { CharacterId = "character:fu-wan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
