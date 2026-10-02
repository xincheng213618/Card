using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundarySunShangxiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-shangxiang", "boundary:jieyin-current") with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:sun-shangxiang", "界孙尚香", "boundary_sun_shangxiang", "boundary:jieyin-current", "wu", 3,
            ["classic:xiaoji"], GeneralGender.Female)
        { CharacterId = "character:sun-shangxiang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
