using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWenPinContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-wen-pin", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:wen-pin", "文聘·OL", "ol-wen-pin", "ol:zhenwei", "wei", 4, [], GeneralGender.Male)
        { CharacterId = "character:wen-pin", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
