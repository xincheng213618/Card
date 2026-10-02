using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryDengAiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-deng-ai", "boundary:tuntian-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-deng-ai", "boundary:zaoxian-current") with
        { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-deng-ai", "boundary:jixi-current") with
        { ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:deng-ai", "界邓艾", "boundary_deng_ai", "boundary:tuntian-current", "wei", 4,
            ["boundary:zaoxian-current"], GeneralGender.Male)
        { CharacterId = "character:deng-ai", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
