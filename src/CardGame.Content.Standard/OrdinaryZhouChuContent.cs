using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryZhouChuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhou-chu", definition => definition.Id switch
        {
            "ol:shanduan" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger },
            _ => definition
        });
        builder.AddGeneral(new("ol:zhou-chu", "周处", "ol-zhou-chu", "ol:shanduan", "jin", 4,
            ["ol:yilie"], GeneralGender.Male)
        { CharacterId = "character:zhou-chu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
