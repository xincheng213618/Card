using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinWangJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-wang-ji", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:wang-ji", "王基", "wang_ji", "classic:qizhi", "wei", 3, ["classic:jinqu"], GeneralGender.Male)
        { CharacterId = "character:wang-ji", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
