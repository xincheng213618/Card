using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryDaQiaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-da-qiao", definition => definition with
        { ExecutionForms = definition.Id == "boundary:guose" ? SkillExecutionForm.None : SkillExecutionForm.Trigger,
          ActionForms = definition.Id == "boundary:guose" ? SkillActionForm.Active : SkillActionForm.None });
        builder.AddGeneral(new("boundary:da-qiao", "界大乔", "boundary_da_qiao", "boundary:guose", "wu", 3,
            ["boundary:liuli"], GeneralGender.Female)
        { CharacterId = "character:da-qiao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
