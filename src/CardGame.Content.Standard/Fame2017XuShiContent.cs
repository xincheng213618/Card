using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XuShiContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:xu-shi"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-xu-shi", "classic:wengua") with { ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.Active });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-xu-shi", "classic:fuzhu") with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:xu-shi", "徐氏", "xu_shi", "classic:wengua", "wu", 3, ["classic:fuzhu"], GeneralGender.Female)
        { CharacterId = "character:xu-shi", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
