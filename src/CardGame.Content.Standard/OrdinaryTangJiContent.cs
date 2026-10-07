using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryTangJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-tang-ji");
        builder.AddGeneral(new("ol:tang-ji", "唐姬", "ol-tang-ji", "ol:kangge", "qun", 3,
            ["ol:jielie"], GeneralGender.Female)
        { CharacterId = "character:tang-ji", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
