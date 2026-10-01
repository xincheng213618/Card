using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016TaoluanContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:zhang-rang"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-zhang-rang", "classic:taoluan") with
        { ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new ContentGeneralDefinition("classic:zhang-rang", "张让", "zhang_rang", "classic:taoluan", "qun", 3, [], GeneralGender.Male)
        { CharacterId = "character:zhang-rang", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
