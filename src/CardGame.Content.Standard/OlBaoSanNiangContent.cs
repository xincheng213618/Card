using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlBaoSanNiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-bao-san-niang", d => d with
        { Tags = d.Id == "ol:wuniang" ? SkillTag.None : SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:bao-san-niang", "鲍三娘·OL", "ol-bao-san-niang", "ol:wuniang", "shu", 4,
            ["ol:zhennan", "ol:xushen"], GeneralGender.Female)
        { CharacterId = "character:bao-san-niang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
