using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryXiaoQiaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-xiao-qiao", definition => definition.Id == "boundary:hongyan"
            ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None }
            : definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("boundary:xiao-qiao", "界小乔", "boundary_xiao_qiao", "boundary:hongyan", "wu", 3,
            ["boundary:tianxiang", "boundary:piaoling"], GeneralGender.Female)
        { CharacterId = "character:xiao-qiao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
