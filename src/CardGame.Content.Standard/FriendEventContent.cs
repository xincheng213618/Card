using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FriendEventContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "friend-events", definition => definition.Id is "friend:shi-tao-gongli" or "friend:cui-jun-gongli"
            ? definition with { Tags = SkillTag.Locked } : definition);
        builder.AddGeneral(new("friend:shi-tao", "友石韬", "friend-shi-tao", "friend:qinying", "qun", 4,
            ["friend:lunxiong", "friend:shi-tao-gongli"], GeneralGender.Male)
        { CharacterId = "character:shi-tao", VariantId = "friend", RulesetId = "sanguosha-mobile" });
        builder.AddGeneral(new("friend:cui-jun", "友崔钧", "friend-cui-jun", "friend:shunyi", "qun", 3,
            ["friend:biwei", "friend:cui-jun-gongli"], GeneralGender.Male)
        { CharacterId = "character:cui-jun", VariantId = "friend", RulesetId = "sanguosha-mobile" });
    }
}
