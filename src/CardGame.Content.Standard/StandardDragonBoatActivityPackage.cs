using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The June 2018 OL four-team treasure race and its independent activity generals.</summary>
public sealed class StandardDragonBoatActivityPackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new("dragonboat-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "dragonboat-activity", skill => skill with
        {
            Tags = skill.Id is "dragonboat:dianhu" or "dragonboat:xingzhao" or "dragonboat:xingzhao-benefit" or "dragonboat:aiye"
                ? SkillTag.Locked : skill.Id == "dragonboat:niepan" ? SkillTag.Limited : SkillTag.None
        });
        AddGeneral("su-fei", "苏飞", "qun", 4, "lianpian");
        AddGeneral("huang-quan", "黄权", "shu", 3, "dianhu", "jianji");
        AddGeneral("tang-zi", "唐咨", "wei", 4, "xingzhao");
        AddGeneral("liu-qi", "刘琦", "shu", 3, "wenji", "tunjiang");
        foreach (var (id, kind) in new (string, CardKind)[]
        {
            ("zong", CardKind.Zong), ("realgar-wine", CardKind.RealgarWine), ("same-boat", CardKind.SameBoat),
            ("strive-upstream", CardKind.StriveUpstream), ("against-current", CardKind.AgainstCurrent)
        })
        {
            var card = CardCatalog.Get(kind);
            builder.AddCard(new($"dragonboat:{id}", card.DisplayName, card.CategoryName, card.Description, kind));
        }
        var bannedFamilies = new HashSet<string>(StringComparer.Ordinal)
        {
            "zuo-ci", "bu-zhi", "ling-ju", "huang-hao", "xun-you", "guo-huanghou", "guo-huang-hou", "zhang-rang",
            "yuan-shao", "li-ru", "cao-pi", "man-chong", "cai-wen-ji", "xu-huang", "da-qiao", "zhang-chun-hua",
            "zhuge-ke", "huang-gai", "han-hao-shi-huan"
        };
        var bannedIds = new HashSet<string>(StringComparer.Ordinal) { "standard:yuan-shu", "classic:yuan-shu", "ol:sp-jia-xu", "sp:jia-xu" };
        var ordinary = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .Where(g => (g.FactionId is "wei" or "shu" or "wu" or "qun") && !bannedIds.Contains(g.Id) &&
                !bannedFamilies.Any(family => g.CharacterId == "character:" + family || g.Id.EndsWith(":" + family, StringComparison.Ordinal)))
            .ToArray();
        var special = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["wei"] = ["dragonboat:huang-quan", "dragonboat:tang-zi"],
            ["shu"] = ["dragonboat:huang-quan", "dragonboat:liu-qi"],
            ["wu"] = ["dragonboat:su-fei", "dragonboat:tang-zi"],
            ["qun"] = ["dragonboat:su-fei", "dragonboat:liu-qi"]
        };
        var pools = special.ToDictionary(pair => "team:" + pair.Key, pair => (IReadOnlyList<string>)ordinary
            .Where(g => g.FactionId == pair.Key).Select(g => g.Id).Concat(pair.Value).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        builder.AddMode(new("activity:dragon-boat-2018", "龙舟争渡·双端2018", 8, 8, new Dictionary<string, int>(),
            "classic:standard-deck", 6, pools.Values.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).ToArray(), ContentModeKind.Team,
            pools.Keys.ToDictionary(team => team, _ => 2, StringComparer.Ordinal))
        { DragonBoatActivity = new(pools, PlayerTier: 5, Rounds: 2) });

        void AddGeneral(string id, string name, string faction, int hp, params string[] skills) => builder.AddGeneral(new(
            "dragonboat:" + id, name, "", "dragonboat:" + skills[0], faction, hp, skills.Skip(1).Select(skill => "dragonboat:" + skill).ToArray())
        { CharacterId = "character:" + id, VariantId = "dragonboat-2018", RulesetId = "dragonboat-2018", Gender = GeneralGender.Male });
    }
}
