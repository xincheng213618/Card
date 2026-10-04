using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryXiahouShiContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b, "boundary-xiahou-shi", definition => definition);
        b.AddGeneral(new("boundary:xiahou-shi", "界夏侯氏", "boundary_xiahou_shi", "boundary:qiaoshi-current", "shu", 3,
            ["boundary:yanyu-current"], GeneralGender.Female)
        { CharacterId = "character:xiahou-shi", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
