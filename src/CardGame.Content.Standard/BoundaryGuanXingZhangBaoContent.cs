using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryGuanXingZhangBaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-guan-xing-zhang-bao", definition =>
            definition.Id == "boundary:fuhun-current"
                ? definition with
                {
                    ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger,
                    ActionForms = SkillActionForm.Active
                }
                : definition);
        builder.AddGeneral(new ContentGeneralDefinition("boundary:guan-xing-zhang-bao", "界关兴张苞",
            "boundary_guan_xing_zhang_bao", "boundary:fuhun-current", "shu", 4)
        { CharacterId = "character:guan-xing-zhang-bao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
