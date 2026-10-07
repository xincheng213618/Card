using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryTianYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-tian-yu", definition => definition);
        builder.AddGeneral(new("ol:tian-yu", "田豫", "ol-tian-yu", "ol:saodi", "wei", 4,
            ["ol:zhuitao"], GeneralGender.Male)
        { CharacterId = "character:tian-yu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
