using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryHanDangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-han-dang", definition => definition);
        builder.AddGeneral(new("boundary:han-dang", "界韩当", "boundary_han_dang", "boundary:gongqi-current", "wu", 4,
            ["boundary:jiefan-current"], GeneralGender.Male)
        { CharacterId = "character:han-dang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
