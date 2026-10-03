using CardGame.Core;

namespace CardGame.Content.Standard;

// Current ordinary OL identity and text: fenglin-twenty-fifth-588-source-2026-10-04.json.
internal static class BoundaryLingTongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-ling-tong", definition => definition with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:ling-tong", "界凌统", "boundary_ling_tong",
            "boundary:xuanfeng-current", "wu", 4, [], GeneralGender.Male)
        { CharacterId = "character:ling-tong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
