using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class MobileChenZhenContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "mobile-chen-zhen");
        builder.AddGeneral(new("mobile:chen-zhen", "陈震·移动版", "mobile-chen-zhen", "mobile:shameng", "shu", 3,
            Gender: GeneralGender.Male)
        { CharacterId = "character:chen-zhen", VariantId = "mobile", RulesetId = "sanguosha-mobile" });
    }
}
