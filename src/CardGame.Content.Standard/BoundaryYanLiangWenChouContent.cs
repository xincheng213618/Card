using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryYanLiangWenChouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-yan-liang-wen-chou", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        // The current official API omits gender. Male supplements the exact
        // already registered classic same-character definition/default only.
        builder.AddGeneral(new("boundary:yan-liang-wen-chou", "界颜良文丑", "boundary_yan_liang_wen_chou",
            "boundary:shuangxiong-current", "qun", 4, [], GeneralGender.Male)
        { CharacterId = "character:yan-liang-wen-chou", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
