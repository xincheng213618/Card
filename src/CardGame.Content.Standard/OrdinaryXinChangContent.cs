using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OrdinaryXinChangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-xin-chang");
        builder.AddGeneral(new("ol:xin-chang", "辛敞", "ol-xin-chang", "ol:canmou", "jin", 3,
            ["ol:congjian"], GeneralGender.Male)
        { CharacterId = "character:xin-chang", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
