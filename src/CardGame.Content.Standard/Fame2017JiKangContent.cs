using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017JiKangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-ji-kang", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:ji-kang", "嵇康", "ji_kang", "classic:qingxian", "wei", 3, ["classic:juexiang"], GeneralGender.Male)
        { CharacterId = "character:ji-kang", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
