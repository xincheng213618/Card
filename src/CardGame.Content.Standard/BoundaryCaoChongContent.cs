using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryCaoChongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-cao-chong", definition => definition);
        builder.AddGeneral(new("boundary:cao-chong", "界曹冲", "boundary_cao_chong", "boundary:chengxiang-current", "wei", 3,
            ["boundary:renxin-current"], GeneralGender.Male)
        { CharacterId = "character:cao-chong", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
