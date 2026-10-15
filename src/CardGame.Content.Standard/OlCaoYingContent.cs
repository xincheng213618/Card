using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlCaoYingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-cao-ying", d => d with
        { Tags = d.Id == "ol:fujian" ? SkillTag.Locked : SkillTag.None, ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:cao-ying", "曹婴·OL", "ol-cao-ying", "ol:lingren", "wei", 4,
            ["ol:fujian"], GeneralGender.Female)
        { CharacterId = "character:cao-ying", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
