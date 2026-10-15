using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlDongBaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-dong-bai", d => d with { ExecutionForms = SkillExecutionForm.None, ActionForms = d.Id == "ol:lianzhu" ? SkillActionForm.Active : SkillActionForm.None, Tags = d.Id == "ol:xiahui" ? SkillTag.Locked : SkillTag.None });
        builder.AddGeneral(new("ol:dong-bai", "董白·OL", "ol-dong-bai", "ol:lianzhu", "qun", 3, ["ol:xiahui"], GeneralGender.Female)
        { CharacterId = "character:dong-bai", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
