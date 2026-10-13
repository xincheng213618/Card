using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlZhangLuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-zhang-lu", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:zhang-lu", "张鲁·OL", "ol-zhang-lu", "ol:yishe", "qun", 3, ["ol:midao", "ol:bushi"], GeneralGender.Male)
        { CharacterId = "character:zhang-lu", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
