using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryLiaoHuaContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-liao-hua", definition => definition.Id switch
        {
            "boundary:dangxian" => definition with { ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger },
            "boundary:fuli" => definition with { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None },
            _ => definition
        });
        builder.AddGeneral(new("boundary:liao-hua", "界廖化", "boundary_liao_hua", "boundary:dangxian", "shu", 4,
            ["boundary:fuli"], GeneralGender.Male)
        { CharacterId = "character:liao-hua", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
