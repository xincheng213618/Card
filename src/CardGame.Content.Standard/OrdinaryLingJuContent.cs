using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryLingJuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-ling-ju", definition => definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddSkill(new("ol:fenxin", "焚心", "锁定技，若场上有已阵亡的：忠臣，“竭缘”减少伤害无体力值限制；反贼，“竭缘”增加伤害无体力值限制；内奸，“竭缘”弃置牌无颜色限制且可以弃置装备区里的牌。")
        { Tags = SkillTag.Locked, ExecutionForms = SkillExecutionForm.State, ActionForms = SkillActionForm.None });
        // Gender is inferred from this current official biography's 女子, not an API gender field.
        builder.AddGeneral(new("ol:ling-ju", "灵雎", "ol-ling-ju", "ol:jieyuan", "qun", 3,
            ["ol:fenxin"], GeneralGender.Female)
        { CharacterId = "character:ling-ju", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
