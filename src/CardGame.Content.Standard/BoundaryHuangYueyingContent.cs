using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryHuangYueyingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-huang-yueying", "boundary:jizhi-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-huang-yueying", "boundary:qicai-current") with
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:huang-yueying", "界黄月英", "boundary_huang_yueying", "boundary:jizhi-current", "shu", 3,
            ["boundary:qicai-current"], GeneralGender.Female)
        { CharacterId = "character:huang-yueying", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
