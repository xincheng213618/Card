using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryDuYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-du-yu", definition => definition.Id switch
        {
            "ol:zhaotao" => definition with { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger },
            _ => definition
        });
        builder.AddGeneral(new("ol:du-yu", "杜预", "ol-du-yu", "ol:sanchen", "jin", 4,
            ["ol:zhaotao"], GeneralGender.Male)
        { CharacterId = "character:du-yu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
