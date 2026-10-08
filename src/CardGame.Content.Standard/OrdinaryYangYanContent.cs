using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryYangYanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-yang-yan");
        builder.AddGeneral(new("ol:yang-yan", "杨艳", "ol-yang-yan", "ol:xianwan", "jin", 3,
            ["ol:xuanbei-current"], GeneralGender.Female)
        { CharacterId = "character:yang-yan", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
