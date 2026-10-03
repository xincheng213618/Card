using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryYuanShaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-yuan-shao", "boundary:luanji-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-yuan-shao", "boundary:xueyi-current") with
        { Tags = SkillTag.Lord, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:yuan-shao", "界袁绍", "boundary_yuan_shao", "boundary:luanji-current", "qun", 4,
            ["boundary:xueyi-current"], GeneralGender.Male)
        { CharacterId = "character:yuan-shao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
