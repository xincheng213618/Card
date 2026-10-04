using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhangBaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zhang-bao", definition =>
            definition.Id == "ol:yingbing" ? definition with { Tags = definition.Tags | SkillTag.Locked } : definition);
        // Official biography 弟/兄 supports Male; the API does not expose a sex field.
        // initial_hp=0 is the official frontend sentinel for a full hp3 start.
        builder.AddGeneral(new("ol:zhang-bao", "张宝", "ol-zhang-bao", "ol:zhoufu", "qun", 3,
            ["ol:yingbing"], GeneralGender.Male)
        { CharacterId = "character:zhang-bao", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
