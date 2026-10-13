using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlTaDunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-ta-dun", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:ta-dun", "蹋顿·OL", "ol-ta-dun", "ol:luanzhan", "qun", 4, [], GeneralGender.Male)
        { CharacterId = "character:ta-dun", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
