using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryGuanYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-guan-yu", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = definition.Id == "boundary:yijue" ? SkillActionForm.Active : SkillActionForm.None });
        builder.AddGeneral(new("boundary:guan-yu", "界关羽", "boundary_guan_yu", "boundary:wusheng", "shu", 4,
            ["boundary:yijue"], GeneralGender.Male)
        { CharacterId = "character:guan-yu", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
