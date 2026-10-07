using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryLiuBianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-liu-bian", definition => definition.Id switch
        {
            "ol:dushi" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None },
            "ol:yuwei" => definition with { Tags = SkillTag.Lord | SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None },
            _ => definition
        });
        builder.AddGeneral(new("ol:liu-bian", "刘辩", "ol-liu-bian", "ol:shiyuan", "qun", 3,
            ["ol:dushi", "ol:yuwei"], GeneralGender.Male)
        { CharacterId = "character:liu-bian", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
