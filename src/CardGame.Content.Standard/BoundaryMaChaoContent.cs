using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryMaChaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-ma-chao", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new ContentGeneralDefinition("boundary:ma-chao", "界马超", "boundary_ma_chao",
            "classic:mashu", "shu", 4, ["boundary:tieqi"], GeneralGender.Male)
        { CharacterId = "character:ma-chao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
