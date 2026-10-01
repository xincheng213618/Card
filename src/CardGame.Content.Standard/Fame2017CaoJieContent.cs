using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017CaoJieContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:cao-jie"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:shouxi", "classic:huimin" })
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-cao-jie", id) with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new ContentGeneralDefinition("classic:cao-jie", "曹节", "cao_jie", "classic:shouxi", "qun", 3, ["classic:huimin"], GeneralGender.Female)
        { CharacterId = "character:cao-jie", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
