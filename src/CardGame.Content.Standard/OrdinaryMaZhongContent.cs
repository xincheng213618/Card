using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryMaZhongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-ma-zhong", definition => definition with
        {
            ActionForms = SkillActionForm.Active,
            ExecutionForms = SkillExecutionForm.Trigger
        });
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:ma-zhong", "马忠", "ol-ma-zhong", "ol:fuman", "shu", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:ma-zhong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
