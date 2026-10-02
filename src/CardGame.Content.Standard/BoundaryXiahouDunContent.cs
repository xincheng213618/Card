using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryXiahouDunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-xiahou-dun", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:xiahou-dun", "界夏侯惇", "boundary_xiahou_dun", "boundary:ganglie", "wei", 4,
            ["boundary:qingjian"], GeneralGender.Male)
        { CharacterId = "character:xiahou-dun", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
