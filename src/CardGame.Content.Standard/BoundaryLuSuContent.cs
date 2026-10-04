using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class BoundaryLuSuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-lu-su");
        builder.AddGeneral(new("boundary:lu-su", "界鲁肃", "boundary_lu_su", "boundary:haoshi-current", "wu", 3,
            ["boundary:dimeng-current"], GeneralGender.Male)
        { CharacterId = "character:lu-su", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
