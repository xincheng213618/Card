using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWangRongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-wang-rong", d => d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:wang-rong", "王荣·OL", "ol-wang-rong", "ol:fengzi", "qun", 3, ["ol:jizhan", "ol:fusong"], GeneralGender.Female)
        { CharacterId = "character:wang-rong", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
