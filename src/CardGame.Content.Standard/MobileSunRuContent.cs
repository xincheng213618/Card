using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class MobileSunRuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "mobile-sun-ru",
            definition => definition with { ExecutionForms = SkillExecutionForm.Trigger },
            new Dictionary<string, SkillTag> { ["mobile:shixin"] = SkillTag.Locked });
        builder.AddGeneral(new("mobile:sun-ru", "孙茹·移动版", "mobile-sun-ru",
            "mobile:yingjian", "wu", 3, ["mobile:shixin"], GeneralGender.Female)
        { CharacterId = "character:sun-ru", VariantId = "mobile", RulesetId = "sanguosha-mobile" });
    }
}
