using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryZhangZhaoZhangHongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhang-zhao-zhang-hong", d =>
            d.Id == "boundary:zhijian" ? d with { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.Active }
                : d with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("boundary:zhang-zhao-zhang-hong", "界张昭张纮", "boundary_zhang_zhao_zhang_hong",
            "boundary:zhijian", "wu", 3, ["boundary:guzheng"], GeneralGender.Male)
        { CharacterId = "character:zhang-zhao-zhang-hong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
