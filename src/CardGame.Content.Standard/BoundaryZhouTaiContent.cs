using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryZhouTaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhou-tai", definition =>
            definition.Id == "boundary:buqu-current"
                ? definition with { ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger }
                : definition);
        builder.AddGeneral(new ContentGeneralDefinition("boundary:zhou-tai", "界周泰",
            "boundary_zhou_tai", "boundary:buqu-current", "wu", 4,
            AdditionalSkillIds: ["boundary:fenji-current"])
        { CharacterId = "character:zhou-tai", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
