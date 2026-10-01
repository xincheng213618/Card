using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinWangPingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-wang-ping", definition =>
            definition with
            {
                Tags = definition.Id == "classic:binglue" ? SkillTag.Locked : SkillTag.None,
                ExecutionForms = definition.Id == "classic:feijun" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
                ActionForms = definition.Id == "classic:feijun" ? SkillActionForm.Active : SkillActionForm.None
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:wang-ping", "王平", "wang_ping", "classic:feijun", "shu", 4, ["classic:binglue"], GeneralGender.Male)
        { CharacterId = "character:wang-ping", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
