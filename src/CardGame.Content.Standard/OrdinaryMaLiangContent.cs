using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryMaLiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-ma-liang", d => d.Id == "ol:zishu"
            ? d with { Tags = d.Tags | SkillTag.Locked } : d);
        // Male follows the current biography; the official structured response has no sex field.
        builder.AddGeneral(new("ol:ma-liang", "马良", "ol-ma-liang", "ol:zishu", "shu", 3,
            ["ol:yingyuan"], GeneralGender.Male)
        { CharacterId = "character:ma-liang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
