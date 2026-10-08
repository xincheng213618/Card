using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OrdinaryJiaChongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-jia-chong", definition => definition.Id == "ol:jianhui"
            ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger } : definition);
        builder.AddGeneral(new("ol:jia-chong", "贾充", "ol-jia-chong", "ol:xiongshu", "jin", 3,
            ["ol:jianhui"], GeneralGender.Male)
        { CharacterId = "character:jia-chong", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
