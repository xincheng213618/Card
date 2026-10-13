using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OlHanSuiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-han-sui",
            definition => definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:han-sui", "韩遂·OL", "ol-han-sui",
            "ol:niluan", "qun", 4, ["ol:xiaoxi"], GeneralGender.Male)
        { CharacterId = "character:han-sui", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
