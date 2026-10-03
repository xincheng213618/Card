using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryMengHuoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-meng-huo", "boundary:huoshou-current") with
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-meng-huo", "boundary:zaiqi-current") with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:meng-huo", "界孟获", "boundary_meng_huo", "boundary:huoshou-current", "shu", 4,
            ["boundary:zaiqi-current"], GeneralGender.Male)
        { CharacterId = "character:meng-huo", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
