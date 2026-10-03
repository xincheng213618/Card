using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryCaiWenJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cai-wen-ji", definition => definition.Id == "boundary:duanchang"
            ? definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger }
            : definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new("boundary:cai-wen-ji", "界蔡文姬", "boundary_cai_wen_ji", "boundary:beige", "qun", 3,
            ["boundary:duanchang"], GeneralGender.Female)
        { CharacterId = "character:cai-wen-ji", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
