using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlDongZhaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-dong-zhao", d => d with
        {
            ExecutionForms = d.Id == "ol:zaowang" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
            ActionForms = d.Id == "ol:zaowang" ? SkillActionForm.Active : SkillActionForm.None,
            Tags = d.Id == "ol:zaowang" ? SkillTag.Limited : SkillTag.None
        });
        builder.AddGeneral(new("ol:dong-zhao", "董昭·OL", "ol-dong-zhao", "ol:xianlue", "wei", 3, ["ol:zaowang"], GeneralGender.Male)
        { CharacterId = "character:dong-zhao", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
