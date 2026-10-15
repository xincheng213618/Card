using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWangLangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-wang-lang", d => d with { ExecutionForms = d.Id == "ol:gushe" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
            ActionForms = d.Id == "ol:gushe" ? SkillActionForm.Active : SkillActionForm.None });
        builder.AddGeneral(new("ol:wang-lang", "王朗·OL", "ol-wang-lang", "ol:gushe", "wei", 3, ["ol:jici"], GeneralGender.Male)
        { CharacterId = "character:wang-lang", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
