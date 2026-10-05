using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhangXingCaiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhang-xing-cai", definition => definition.Id switch
        {
            "ol:shenxian" => definition with { ExecutionForms = SkillExecutionForm.Trigger },
            "ol:qiangwu" => definition with { ActionForms = SkillActionForm.Active },
            _ => definition
        });
        // Current API initial_hp=0 is the full-HP sentinel. Female is inferred from the official bio.
        builder.AddGeneral(new("ol:zhang-xing-cai", "张星彩", "ol-zhang-xing-cai", "ol:shenxian", "shu", 3,
            ["ol:qiangwu"], GeneralGender.Female)
        { CharacterId = "character:zhang-xing-cai", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
