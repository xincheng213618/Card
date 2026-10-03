using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryCaoZhangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cao-zhang", definition => definition);
        builder.AddGeneral(new("boundary:cao-zhang", "界曹彰", "boundary_cao_zhang",
            "boundary:jiangchi", "wei", 4, [], GeneralGender.Male)
        { CharacterId = "character:cao-zhang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
