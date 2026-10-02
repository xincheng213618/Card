using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryZhangFeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-slash-stage", "boundary:paoxiao") with
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhang-fei", "boundary:tishen") with
        { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:zhang-fei", "界张飞", "boundary_zhang_fei", "boundary:paoxiao", "shu", 4,
            ["boundary:tishen"], GeneralGender.Male)
        { CharacterId = "character:zhang-fei", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
