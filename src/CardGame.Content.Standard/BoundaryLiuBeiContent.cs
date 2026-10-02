using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryLiuBeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-liu-bei", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("batch9-support", "boundary:jijiang") with
        { Tags = SkillTag.Lord, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:liu-bei", "界刘备", "boundary_liu_bei", "boundary:rende", "shu", 4,
            ["boundary:jijiang"], GeneralGender.Male)
        { CharacterId = "character:liu-bei", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
