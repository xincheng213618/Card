using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryYuFanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-yu-fan", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:yu-fan", "界虞翻", "boundary_yu_fan", "boundary:zongxuan-current", "wu", 3,
            ["boundary:zhiyan-current"], GeneralGender.Male)
        { CharacterId = "character:yu-fan", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
