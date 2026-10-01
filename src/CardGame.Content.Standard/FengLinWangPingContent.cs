using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinWangPingContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:wang-ping"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-wang-ping", "classic:feijun") with { ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-wang-ping", "classic:binglue") with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:wang-ping", "王平", "wang_ping", "classic:feijun", "shu", 4, ["classic:binglue"], GeneralGender.Male)
        { CharacterId = "character:wang-ping", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
