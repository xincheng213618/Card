using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryCaoRenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cao-ren");
        builder.AddGeneral(new ContentGeneralDefinition("boundary:cao-ren", "界曹仁",
            "boundary_cao_ren", "boundary:jushou-current", "wei", 4,
            AdditionalSkillIds: ["boundary:jiewei-current"])
        { CharacterId = "character:cao-ren", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
