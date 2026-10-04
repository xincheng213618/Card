using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryLiuBiaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-liu-biao", definition =>
            definition.Id == "boundary:zongshi-current"
                ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger | SkillExecutionForm.State }
                : definition);
        builder.AddGeneral(new("boundary:liu-biao", "界刘表", "boundary_liu_biao", "boundary:zishou-current", "qun", 3,
            ["boundary:zongshi-current"], GeneralGender.Male)
        { CharacterId = "character:liu-biao", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
