using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundarySunQuanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-sun-quan", definition => definition with
        {
            Tags = definition.Id == "boundary:jiuyuan" ? SkillTag.Lord : SkillTag.None,
            ExecutionForms = definition.Id == "boundary:jiuyuan" ? SkillExecutionForm.State : SkillExecutionForm.None,
            ActionForms = definition.Id == "boundary:zhiheng" ? SkillActionForm.Active : SkillActionForm.None
        });
        builder.AddGeneral(new("boundary:sun-quan", "界孙权", "boundary_sun_quan", "boundary:zhiheng", "wu", 4,
            ["boundary:jiuyuan"], GeneralGender.Male)
        { CharacterId = "character:sun-quan", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
