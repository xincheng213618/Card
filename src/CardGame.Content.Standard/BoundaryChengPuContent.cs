using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryChengPuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cheng-pu", definition => definition with
        { ExecutionForms = definition.Id == "boundary:lihuo"
            ? SkillExecutionForm.State | SkillExecutionForm.Trigger
            : SkillExecutionForm.Trigger });
        builder.AddGeneral(new("boundary:cheng-pu", "界程普", "boundary_cheng_pu", "boundary:lihuo", "wu", 4,
            ["boundary:chunlao"], GeneralGender.Male)
        { CharacterId = "character:cheng-pu", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
