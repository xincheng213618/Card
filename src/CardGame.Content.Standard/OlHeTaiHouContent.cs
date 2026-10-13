using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OlHeTaiHouContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-he-tai-hou",
            definition => definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:he-tai-hou", "何太后·OL", "ol-he-tai-hou", "ol:zhendu", "qun", 3,
            ["ol:qiluan"], GeneralGender.Female)
        { CharacterId = "character:he-tai-hou", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
