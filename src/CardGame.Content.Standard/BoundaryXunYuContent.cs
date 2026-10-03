using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryXunYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xun-yu", "boundary:quhu-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xun-yu", "boundary:jieming-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:xun-yu", "界荀彧", "boundary_xun_yu", "boundary:quhu-current", "wei", 3,
            ["boundary:jieming-current"], GeneralGender.Male)
        { CharacterId = "character:xun-yu", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
