using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryZhuRongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhu-rong", "boundary:juxiang-current") with
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhu-rong", "boundary:lieren-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhu-rong", "boundary:changbiao-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:zhu-rong", "界祝融", "boundary_zhu_rong", "boundary:juxiang-current", "shu", 4,
            ["boundary:lieren-current", "boundary:changbiao-current"], GeneralGender.Female)
        { CharacterId = "character:zhu-rong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
