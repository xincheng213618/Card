using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinarySimaLangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sima-lang", definition => definition.Id switch
        {
            "ol:junbing" => definition with { ExecutionForms = SkillExecutionForm.Trigger },
            "ol:quji" => definition with { ActionForms = SkillActionForm.Active },
            _ => definition
        });
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:sima-lang", "司马朗", "ol-sima-lang", "ol:junbing", "wei", 3,
            ["ol:quji"], GeneralGender.Male)
        { CharacterId = "character:sima-lang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
