using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryFuHuangHouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-fu-huang-hou", d => d with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        // API gender is absent; official biography explicitly identifies the
        // empress, consistent with the already registered classic same person.
        builder.AddGeneral(new("boundary:fu-huang-hou", "界伏皇后", "boundary_fu_huang_hou",
            "boundary:zhuikong-current", "qun", 3, ["boundary:qiuyuan-current"], GeneralGender.Female)
        { CharacterId = "character:fu-huang-hou", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
