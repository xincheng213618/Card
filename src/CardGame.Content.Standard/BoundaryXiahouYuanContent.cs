using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryXiahouYuanContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-xiahou-yuan", definition => definition);
        builder.AddGeneral(new("boundary:xiahou-yuan", "界夏侯渊", "boundary_xiahou_yuan",
            "boundary:shensu", "wei", 4, ["boundary:shebian"], GeneralGender.Male)
        { CharacterId = "character:xiahou-yuan", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
