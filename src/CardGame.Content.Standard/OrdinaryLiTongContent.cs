using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryLiTongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-li-tong", definition => definition with
        {
            ExecutionForms = SkillExecutionForm.Trigger
        });
        // Male follows the historical identity and official portrait; the API has no sex field.
        builder.AddGeneral(new("ol:li-tong", "李通", "ol-li-tong", "ol:tuifeng", "wei", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:li-tong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
