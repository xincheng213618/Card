using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryXuShuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xu-shu", "boundary:zhuhai-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xu-shu", "boundary:qianxin-current") with
        { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xu-shu", "boundary:jianyan-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:xu-shu", "界徐庶", "boundary_xu_shu", "boundary:zhuhai-current", "shu", 4,
            ["boundary:qianxin-current"], GeneralGender.Male)
        { CharacterId = "character:xu-shu", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
