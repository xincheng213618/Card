using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryLiDianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "boundary:xunxun", "boundary:wangxi" })
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-li-dian", id) with
            { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:li-dian", "界李典", "boundary_li_dian", "boundary:xunxun", "wei", 3,
            ["boundary:wangxi"], GeneralGender.Male)
        { CharacterId = "character:li-dian", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
