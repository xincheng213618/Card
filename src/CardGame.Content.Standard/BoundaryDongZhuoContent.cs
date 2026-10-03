using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryDongZhuoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-dong-zhuo", definition => definition.Id switch
        {
            "boundary:roulin-current" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State },
            "boundary:benghuai-current" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger },
            "boundary:baonue-current" => definition with { Tags = SkillTag.Lord, ExecutionForms = SkillExecutionForm.Trigger },
            _ => definition
        });
        builder.AddGeneral(new("boundary:dong-zhuo", "界董卓", "boundary_dong_zhuo", "boundary:jiuchi-current", "qun", 8,
            ["boundary:roulin-current", "boundary:benghuai-current", "boundary:baonue-current"], GeneralGender.Male)
        { CharacterId = "character:dong-zhuo", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
