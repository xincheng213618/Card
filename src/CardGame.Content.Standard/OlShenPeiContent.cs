using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OlShenPeiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-shen-pei", definition => definition with
        { ExecutionForms = definition.Id == "ol:gangzhi" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
          Tags = definition.Id == "ol:gangzhi" ? SkillTag.Locked : SkillTag.None });
        builder.AddGeneral(new("ol:shen-pei", "审配·OL", "ol-shen-pei", "ol:gangzhi", "qun", 3,
            ["ol:beizhan"], GeneralGender.Male)
        { CharacterId = "character:shen-pei", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
