using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryGuanYinPingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-guan-yin-ping", definition => definition,
            new Dictionary<string, SkillTag> { ["ol:huxiao"] = SkillTag.Locked, ["ol:wuji"] = SkillTag.Awakening });
        builder.AddGeneral(new("ol:guan-yin-ping", "关银屏", "ol-guan-yin-ping", "ol:xuehen", "shu", 3,
            ["ol:huxiao", "ol:wuji"], GeneralGender.Female)
        { CharacterId = "character:guan-yin-ping", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
