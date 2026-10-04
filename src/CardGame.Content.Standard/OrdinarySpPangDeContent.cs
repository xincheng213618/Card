using CardGame.Core;

namespace CardGame.Content.Standard;
internal static class OrdinarySpPangDeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-sp-pang-de", definition => definition);
        builder.AddGeneral(new("ol:sp-pang-de", "SP庞德", "ol-sp-pang-de", "classic:mashu", "wei", 4,
            ["ol:juesi"], GeneralGender.Male)
        { CharacterId = "character:pang-de", VariantId = "sp", RulesetId = "sanguosha-ol" });
    }
}
