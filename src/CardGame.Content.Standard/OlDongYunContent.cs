using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OlDongYunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-dong-yun", definition => definition with
            { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:dong-yun", "董允·OL", "ol-dong-yun", "ol:bingzheng", "shu", 3,
            ["ol:sheyan"], GeneralGender.Male)
        { CharacterId = "character:dong-yun", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
