using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryChenGongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-chen-gong", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active });
        builder.AddGeneral(new("boundary:chen-gong", "界陈宫", "boundary_chen_gong",
            "boundary:mingce-current", "qun", 3, ["classic:zhichi"], GeneralGender.Male)
        { CharacterId = "character:chen-gong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
