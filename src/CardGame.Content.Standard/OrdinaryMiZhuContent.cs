using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryMiZhuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-mi-zhu", definition => definition.Id switch
        {
            "ol:ziyuan" => definition with { ActionForms = SkillActionForm.Active },
            "ol:jujia" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State },
            _ => definition
        });
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:mi-zhu", "糜竺", "ol-mi-zhu", "ol:ziyuan", "shu", 3,
            ["ol:jujia"], GeneralGender.Male)
        { CharacterId = "character:mi-zhu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
