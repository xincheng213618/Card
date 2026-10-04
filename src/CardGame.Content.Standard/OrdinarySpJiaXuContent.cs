using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinarySpJiaXuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-jia-xu", definition => definition.Id switch
        {
            "ol:zhenlue" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State },
            "ol:jianshu" => definition with { Tags = SkillTag.Limited, ActionForms = SkillActionForm.Active },
            "ol:yongdi" => definition with { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger },
            _ => definition
        });
        // API initial_hp=0 means full HP. Male follows the historical identity
        // and existing metadata; the current API supplies no structured sex.
        builder.AddGeneral(new("ol:sp-jia-xu", "SP贾诩", "ol-sp-jia-xu", "ol:zhenlue", "wei", 3,
            ["ol:jianshu", "ol:yongdi"], GeneralGender.Male)
        { CharacterId = "character:jia-xu", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
