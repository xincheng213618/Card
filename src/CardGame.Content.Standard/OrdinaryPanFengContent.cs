using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryPanFengContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-pan-feng", definition => definition);
        builder.AddGeneral(new("ol:pan-feng", "潘凤", "ordinary-pan-feng", "ol:kuangfu", "qun", 4,
            [], GeneralGender.Male)
        { CharacterId = "character:pan-feng", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
