using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryChenLinContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-chen-lin", definition => definition);
        builder.AddGeneral(new("ol:chen-lin", "陈琳", "ol-chen-lin", "ol:bifa", "wei", 3,
            ["ol:songci"], GeneralGender.Male)
        { CharacterId = "character:chen-lin", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
