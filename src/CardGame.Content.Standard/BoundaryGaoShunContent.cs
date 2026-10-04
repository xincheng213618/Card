using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryGaoShunContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-gao-shun", definition => definition.Id == "boundary:jinjiu-current"
                ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None }
            : definition);
        builder.AddGeneral(new("boundary:gao-shun", "界高顺", "boundary_gao_shun", "boundary:xianzhen-current", "qun", 4,
            ["boundary:jinjiu-current"], GeneralGender.Male)
        { CharacterId = "character:gao-shun", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
