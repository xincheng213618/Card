using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlShiXieContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-shi-xie", d => d with
        { ExecutionForms = SkillExecutionForm.Trigger, Tags = d.Id == "ol:lixia" ? SkillTag.Locked : SkillTag.None });
        builder.AddGeneral(new("ol:shi-xie", "士燮·OL", "ol-shi-xie", "ol:biluan", "qun", 3,
            ["ol:lixia"], GeneralGender.Male)
        { CharacterId = "character:shi-xie", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
