using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryYueJinContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-yue-jin");
        builder.AddGeneral(new("ol:yue-jin", "乐进", "ol-yue-jin", "ol:xiaoguo", "wei", 4,
            Gender: GeneralGender.Male)
        { CharacterId = "character:yue-jin", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
