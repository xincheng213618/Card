using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017CaoJieContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-cao-jie", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new ContentGeneralDefinition("classic:cao-jie", "曹节", "cao_jie", "classic:shouxi", "qun", 3, ["classic:huimin"], GeneralGender.Female)
        { CharacterId = "character:cao-jie", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
