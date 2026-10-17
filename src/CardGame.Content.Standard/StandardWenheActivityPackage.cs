using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The anniversary July 2019 eight-player survival activity.</summary>
public sealed class StandardWenheActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("wenhe-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "wenhe-activity", skill => skill with
        {
            Tags = skill.Id == "wenhe:niepan" ? SkillTag.Limited :
                skill.Id == "wenhe:rules" ? SkillTag.Locked : SkillTag.None
        });
        AddCard("star-shift", "斗转星移", "随机分配你与一名其他角色的体力值，至少为1且不能超出各自体力上限。", CardKind.StarShift);
        AddCard("beam-swap", "偷梁换柱", "对自己使用，随机分配所有角色装备区里的牌。", CardKind.BeamSwap);
        AddCard("hand-redistribution", "李代桃僵", "随机分配你与一名其他角色的手牌。", CardKind.HandRedistribution);
        AddCard("chaos", "文和乱武", "除你以外的所有其他角色依次向距离最近的其他角色使用杀，否则失去1点体力。", CardKind.WenheChaos);
        builder.AddCard(new("wenhe:poison", "毒", "基本牌", "不能直接使用；宴安鸩毒轮中，桃视为毒，毒正面朝上离开手牌区时失去1点体力。", CardKind.Poison));

        // Match actual family metadata and preserve historical null-identity registrations.
        // Ordinary Zhuge Liang and Gan Ning remain legal; only the named Wolong and boundary Gan Ning are banned.
        var bannedFamilies = new HashSet<string>(StringComparer.Ordinal)
        {
            "hua-tuo", "liu-bei", "yu-ji", "zuo-ci", "xu-huang", "wolong-zhuge-liang", "cao-ren", "da-qiao",
            "meng-huo", "xun-you", "guo-huanghou", "guo-huang-hou", "zhuge-dan", "chen-gong", "xiao-qiao",
            "zhou-tai", "lu-ji", "zhuge-zhan", "dong-zhuo", "wu-tu-gu", "pang-tong", "dong-yun", "sun-lu-ban",
            "ta-dun", "yuan-shao", "cao-pi", "man-chong", "jia-xu", "cai-wen-ji", "ling-ju", "bu-zhi", "cao-jie", "liu-yan"
        };
        var players = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => g.FactionId != "god" && g.Id != "boundary:gan-ning" && !bannedFamilies.Any(family =>
                g.CharacterId == $"character:{family}" || g.Id.EndsWith($":{family}", StringComparison.Ordinal)))
            .Select(g => g.Id).Distinct(StringComparer.Ordinal).ToArray();
        builder.AddMode(new("activity:wenhe-2019", "文和乱武·十周年2019", 8, 8, new Dictionary<string, int>(),
            "classic:standard-deck", 5, players, ContentModeKind.Survival)
        { WenheActivity = new(PlayerTier: 5, EnableAspirations: true) });

        void AddCard(string id, string name, string description, CardKind kind) =>
            builder.AddCard(new($"wenhe:{id}", name, "锦囊", description, kind));
    }
}
