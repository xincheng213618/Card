using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlYanBaiHuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-yan-bai-hu", d => d with
        { ExecutionForms = SkillExecutionForm.Trigger, Tags = SkillTag.Locked });
        builder.AddGeneral(new("ol:yan-bai-hu", "严白虎·OL", "ol-yan-bai-hu", "ol:zhidao", "qun", 4,
            ["ol:jili"], GeneralGender.Male)
        { CharacterId = "character:yan-bai-hu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
