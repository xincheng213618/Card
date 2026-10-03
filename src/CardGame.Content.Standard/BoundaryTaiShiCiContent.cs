using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryTaiShiCiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-taishi-ci", "boundary:tianyi") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-taishi-ci", "boundary:hanzhan") with
        { ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:taishi-ci", "界太史慈", "boundary_taishi_ci", "boundary:tianyi", "wu", 4,
            ["boundary:hanzhan"], GeneralGender.Male)
        { CharacterId = "character:taishi-ci", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
