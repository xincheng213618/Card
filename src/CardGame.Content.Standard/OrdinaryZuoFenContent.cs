using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryZuoFenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zuo-fen");
        builder.AddGeneral(new("ol:zuo-fen", "左棻", "ol-zuo-fen", "ol:zhaosong", "jin", 3,
            ["ol:lisi"], GeneralGender.Female)
        { CharacterId = "character:zuo-fen", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
