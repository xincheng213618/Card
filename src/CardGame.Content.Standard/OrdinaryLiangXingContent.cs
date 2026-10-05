using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryLiangXingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        // WIP (round 8): LuLve is assembled; ZhuanXi's face-state damage modifier
        // and the third-party virtual slash branch are still pending.
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-liang-xing", definition => definition);
        builder.AddGeneral(new("ol:liang-xing", "梁兴", "ol-liang-xing", "ol:luelve", "qun", 4, null, GeneralGender.Male)
        { CharacterId = "character:liang-xing", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
