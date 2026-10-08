using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryYangHuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-yang-hu", definition => definition.Id switch
        {
            "ol:dezhang" => definition with { Tags = SkillTag.Awakening, ExecutionForms = SkillExecutionForm.Trigger },
            "ol:weishu" => definition with { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.Trigger },
            _ => definition
        });
        builder.AddGeneral(new("ol:yang-hu", "羊祜", "ol-yang-hu", "ol:huaiyuan", "jin", 4,
            ["ol:chongxin", "ol:dezhang"], GeneralGender.Male)
        { CharacterId = "character:yang-hu", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
