using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhugeJinContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhuge-jin", d => d.Id == "ol:mingzhe"
            ? d with { Tags = d.Tags | SkillTag.Locked }
            : d);
        // Male is the current biography's brother/father inference; the API has no sex field.
        builder.AddGeneral(new("ol:zhuge-jin", "诸葛瑾", "ol-zhuge-jin", "ol:huanshi", "wu", 3,
            ["ol:hongyuan", "ol:mingzhe"], GeneralGender.Male)
        { CharacterId = "character:zhuge-jin", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
