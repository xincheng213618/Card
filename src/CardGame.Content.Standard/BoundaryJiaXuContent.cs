using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryJiaXuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "boundary:wansha", "boundary:weimu" })
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-jia-xu", id) with
            { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-jia-xu", "boundary:luanwu") with
        { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:jia-xu", "界贾诩", "boundary_jia_xu", "boundary:wansha", "qun", 3,
            ["boundary:luanwu", "boundary:weimu"], GeneralGender.Male)
        { CharacterId = "character:jia-xu", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
