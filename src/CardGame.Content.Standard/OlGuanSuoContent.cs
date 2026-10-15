using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlGuanSuoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-guan-suo", tagOverrides: new Dictionary<string, SkillTag> { ["ol:xiefang"] = SkillTag.Locked });
        builder.AddGeneral(new("ol:guan-suo", "关索·OL", "ol-guan-suo", "ol:zhengnan", "shu", 4, ["ol:xiefang"], GeneralGender.Male)
        { CharacterId = "character:guan-suo", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
