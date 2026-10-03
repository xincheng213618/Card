using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryJiangWeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-jiang-wei", "boundary:tiaoxin-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-jiang-wei", "boundary:zhiji-current") with
        { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:jiang-wei", "界姜维", "boundary_jiang_wei", "boundary:tiaoxin-current", "shu", 4,
            ["boundary:zhiji-current"], GeneralGender.Male)
        { CharacterId = "character:jiang-wei", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
