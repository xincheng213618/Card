using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OlXiahouBaContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-xiahou-ba",
            definition => definition with { ExecutionForms = SkillExecutionForm.State },
            new Dictionary<string, SkillTag> { ["ol:baobian"] = SkillTag.Locked });
        builder.AddGeneral(new("ol:xiahou-ba", "夏侯霸·OL", "ol-xiahou-ba", "ol:baobian", "shu", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:xiahou-ba", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
