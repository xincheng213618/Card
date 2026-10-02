using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryZhugeLiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhuge-liang", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:zhuge-liang", "界诸葛亮", "boundary_zhuge_liang", "boundary:guanxing-current", "shu", 3,
            ["classic:kongcheng"], GeneralGender.Male)
        { CharacterId = "character:zhuge-liang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
