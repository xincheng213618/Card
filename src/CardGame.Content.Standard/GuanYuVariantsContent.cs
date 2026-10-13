using CardGame.Core;

namespace CardGame.Content.Standard;

// Distinct published rulesets share a character identity, never a skill definition.
internal static class GuanYuVariantsContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "guan-yu-variants", definition => definition,
            new Dictionary<string, SkillTag>
            {
                ["mou-mobile:yijue"] = SkillTag.Locked,
                ["mou-ol:duoshou"] = SkillTag.Locked,
                ["magic:weiwo"] = SkillTag.Limited,
                ["magic:wushen"] = SkillTag.Locked
            });
        Add("mou:guan-yu-mobile", "谋关羽·移动版", "mou-guan-yu-mobile", "mou-mobile:wusheng", "mou-mobile:yijue", 4, "mou-mobile", "sanguosha-mobile", "shu");
        Add("mou:guan-yu-ol", "谋关羽·OL", "mou-guan-yu-ol", "mou-ol:weilin", "mou-ol:duoshou", 4, "mou-ol", "sanguosha-ol", "shu");
        Add("magic:guan-yu", "魔关羽", "magic-guan-yu", "magic:hanguo", "magic:weiwo", 5, "magic", "sanguosha-offline-man", "god");

        void Add(string id, string name, string portrait, string first, string second, int hp, string variant, string ruleset, string faction) =>
            builder.AddGeneral(new(id, name, portrait, first, faction, hp, [second], GeneralGender.Male)
            { CharacterId = "character:guan-yu", VariantId = variant, RulesetId = ruleset });
    }
}
