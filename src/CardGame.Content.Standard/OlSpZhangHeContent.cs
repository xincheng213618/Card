using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlSpZhangHeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-sp-zhang-he", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:sp-zhang-he", "SP张郃·OL", "ol-sp-zhang-he", "ol:zhouxuan", "qun", 4, [], GeneralGender.Male)
        { CharacterId = "character:zhang-he", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
