using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryLuMengContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-lu-meng", definition => definition with
        {
            Tags = definition.Id == "boundary:qinxue" ? SkillTag.Awakening : SkillTag.None,
            ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None
        });
        builder.AddGeneral(new("boundary:lu-meng", "界吕蒙", "boundary_lu_meng", "boundary:keji", "wu", 4,
            ["boundary:qinxue", "boundary:botu"], GeneralGender.Male)
        { CharacterId = "character:lu-meng", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
