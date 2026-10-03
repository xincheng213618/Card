using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundarySunCeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-ce", "boundary:jiang-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-ce", "boundary:hunzi-current") with
        { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-ce", "boundary:zhiba-current") with
        { Tags = SkillTag.Lord, ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:sun-ce", "界孙策", "boundary_sun_ce", "boundary:jiang-current", "wu", 4,
            ["boundary:hunzi-current", "boundary:zhiba-current"], GeneralGender.Male)
        { CharacterId = "character:sun-ce", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
