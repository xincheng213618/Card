using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class FriendTrioContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "friend-trio", tagOverrides: new Dictionary<string, SkillTag>
        {
            ["friend:qihui"] = SkillTag.Locked
        });
        builder.AddSkill(new("friend:fangqiu", "方遒", "限定技，当你执行卧龙演策后，你可以展示预测。本次预测全部验证后，执行效果的值均+1；若预测牌数大于3且全部正确，此技能可再次发动。") { Tags = SkillTag.Limited });
        builder.AddSkill(new("friend:gongli-zhuge-liang", "共砺", "身份模式无效。排位、斗地主：同阵营友庞统在场时预测牌数+1；同阵营友徐庶在场时第一张预测视为正确。") { Tags = SkillTag.Locked });
        builder.AddSkill(new("friend:gongli-pang-tong", "共砺", "身份模式无效。排位、斗地主：同阵营友诸葛亮在场时养名牌数+1；同阵营友徐庶在场时养名后获得一张本次未使用花色的牌。") { Tags = SkillTag.Locked });
        builder.AddSkill(new("friend:gongli-xu-shu", "共砺", "身份模式无效。排位、斗地主：同阵营友诸葛亮在场时玄剑改为一张手牌当杀；同阵营友庞统在场时玄剑的杀无距离限制。") { Tags = SkillTag.Locked });
        Add("zhuge-liang", "友诸葛亮", ["friend:yance", "friend:fangqiu", "friend:gongli-zhuge-liang"]);
        Add("pang-tong", "友庞统", ["friend:manjuan", "friend:yangming", "friend:gongli-pang-tong"]);
        Add("xu-shu", "友徐庶", ["friend:xiaxing", "friend:qihui", "friend:gongli-xu-shu"]);
        void Add(string id, string name, string[] skills) => builder.AddGeneral(new("friend:" + id, name,
            "friend-" + id, skills[0], "qun", 3, skills.Skip(1).ToArray(), GeneralGender.Male)
        { CharacterId = "character:" + id, VariantId = "friend", RulesetId = "sanguosha-mobile" });
    }
}
