using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinGuanqiuJianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-guanqiu-jian", definition =>
            definition with
            {
                Tags = definition.Id == "classic:hongju" ? SkillTag.Awakening : SkillTag.None,
                ExecutionForms = definition.Id == "classic:qingce" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
                ActionForms = definition.Id == "classic:qingce" ? SkillActionForm.Active : SkillActionForm.None
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:guanqiu-jian", "毌丘俭", "guanqiu_jian", "classic:zhengrong", "wei", 4, ["classic:hongju"], GeneralGender.Male)
        { CharacterId = "character:guanqiu-jian", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
