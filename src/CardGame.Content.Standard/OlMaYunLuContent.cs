using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlMaYunLuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-ma-yun-lu", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:ma-yun-lu", "马云騄·OL", "ol-ma-yun-lu", "classic:mashu", "shu", 4, ["ol:fengpo"], GeneralGender.Female)
        { CharacterId = "character:ma-yun-lu", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
