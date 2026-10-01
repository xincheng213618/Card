using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016ConversionContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-guo-huanghou", definition =>
            definition with
            {
                ExecutionForms = definition.Id == "classic:danxin" ? SkillExecutionForm.Trigger : SkillExecutionForm.State,
                ActionForms = definition.Id == "classic:jiaozhao" ? SkillActionForm.Active : SkillActionForm.None
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:guo-huanghou", "郭皇后", "guo_huanghou",
            "classic:jiaozhao", "wei", 3, ["classic:danxin"], GeneralGender.Female)
        { CharacterId = "character:guo-huanghou", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
