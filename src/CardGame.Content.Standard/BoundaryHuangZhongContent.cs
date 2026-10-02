using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryHuangZhongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-huang-zhong", "boundary:liegong-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:huang-zhong", "界黄忠", "boundary_huang_zhong", "boundary:liegong-current", "shu", 4, Gender: GeneralGender.Male)
        { CharacterId = "character:huang-zhong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
