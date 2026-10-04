using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryYuJinContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-yu-jin", definition => definition.Id == "boundary:yizhong-current"
            ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None }
            : definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        // The current API has no gender field. Retain the existing classic same
        // person's Male identity; initial_hp=0 does not replace the public HP4.
        builder.AddGeneral(new("boundary:yu-jin", "界于禁", "boundary_yu_jin", "boundary:zhenjun-current", "wei", 4,
            ["boundary:yizhong-current"], GeneralGender.Male)
        { CharacterId = "character:yu-jin", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
