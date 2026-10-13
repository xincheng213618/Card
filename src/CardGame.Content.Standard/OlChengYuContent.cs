using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlChengYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-cheng-yu", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:cheng-yu", "程昱·OL", "ol-cheng-yu", "ol:shefu", "wei", 3, ["ol:benyu"], GeneralGender.Male)
        { CharacterId = "character:cheng-yu", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
