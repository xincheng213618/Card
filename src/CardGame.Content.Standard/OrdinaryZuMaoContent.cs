using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZuMaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ordinary-zu-mao", definition =>
            definition.Id == "ol:juedi" ? definition with { Tags = definition.Tags | SkillTag.Locked } : definition);
        // Male is the documented historical/portrait editorial judgment; no structured sex field.
        // The current frontend treats initial_hp=0 as a full max-hp start.
        builder.AddGeneral(new("ol:zu-mao", "祖茂", "ol-zu-mao", "ol:yinbing", "wu", 4,
            ["ol:juedi"], GeneralGender.Male)
        { CharacterId = "character:zu-mao", VariantId = "ordinary", RulesetId = "sanguosha-ol" });
    }
}
