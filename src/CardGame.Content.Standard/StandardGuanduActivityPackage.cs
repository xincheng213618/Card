using CardGame.Core;
using System.Collections.ObjectModel;

namespace CardGame.Content.Standard;

/// <summary>The June 2023 OL roster. Activity editions never replace an ordinary general.</summary>
public sealed class StandardGuanduActivityPackage : IGameContentPackage
{
    public const string ModeId = "activity:guandu-2023";
    public PackageManifest Manifest { get; } = new("guandu-activity", new Version(1, 0, 0),
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public static IReadOnlyDictionary<string, string> OrdinaryVersions { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yuan-shao"] = "boundary:yuan-shao", ["chen-lin"] = "ol:chen-lin",
        ["yan-liang-wen-chou"] = "classic:yan-liang-wen-chou", ["ju-shou"] = "classic:ju-shou",
        ["guo-tu-feng-ji"] = "classic:guo-tu-feng-ji", ["liu-bei"] = "boundary:liu-bei",
        ["yuan-tan-yuan-shang"] = "ol:yuan-tan-yuan-shang", ["lv-kuang-lv-xiang"] = "ol:lv-kuang-lv-xiang",
        ["xin-pi"] = "ol:xin-pi", ["gao-gan"] = "ol:gao-gan",
        ["cao-cao"] = "boundary:cao-cao", ["xun-yu"] = "classic:xun-yu", ["xun-you"] = "classic:xun-you",
        ["guo-jia"] = "boundary:guo-jia", ["xu-huang"] = "classic:xu-huang", ["yu-jin"] = "classic:yu-jin",
        ["zhang-liao"] = "boundary:zhang-liao", ["guan-yu"] = "sp:guan-yu",
        ["han-hao-shi-huan"] = "classic:han-hao-shi-huan", ["cao-ren"] = "classic:cao-ren",
        ["cao-hong"] = "ol:cao-hong", ["cheng-yu"] = "ol:cheng-yu", ["man-chong"] = "classic:man-chong",
        ["li-tong"] = "ol:li-tong", ["zhang-xiu"] = "classic:zhang-xiu", ["jia-xu"] = "ol:sp-jia-xu",
        ["xiahou-yuan"] = "boundary:xiahou-yuan"
    });

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "guandu-activity", skill => skill with
        {
            Tags = skill.Id switch
            {
                "guandu:fushi" or "guandu:quanjiu" or "guandu:cangchu" or "guandu:liangying" or "guandu:polu" or "guandu:gangzhi" or "guandu:suishi" => SkillTag.Locked,
                "guandu:yongdi" or "guandu:niepan" => SkillTag.Limited,
                _ => SkillTag.None
            }
        });
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "guandu-events", skill => skill with { Tags = SkillTag.Locked });
        var ordinary = CharacterContentCatalog.Default.GetPoolGenerals("standard-classic-generals", "current")
            .ToDictionary(g => g.Id, StringComparer.Ordinal);
        foreach (var (family, source) in OrdinaryVersions)
        {
            var general = ordinary[source];
            builder.AddGeneral(general with
            {
                Id = "guandu:" + family,
                FactionId = family is "chen-lin" or "liu-bei" ? "qun" : family == "zhang-xiu" ? "wei" : general.FactionId,
                CharacterId = general.CharacterId ?? "character:" + family,
                VariantId = "guandu-2023", RulesetId = "guandu-2023"
            });
        }
        Add("xu-you", "许攸", "qun", 3, "shicai", "fushi", "zezhu", "chenggong");
        Add("han-meng", "韩猛", "qun", 4, "jieliang", "quanjiu");
        Add("xin-ping", "辛评", "qun", 3, "fuyuan", "zhongjie", "yongdi");
        Add("chun-yu-qiong", "淳于琼", "qun", 5, "cangchu", "sushou", "liangying");
        Add("liu-ye", "刘晔", "wei", 3, "polu", "choulue");
        Add("zang-ba", "臧霸", "wei", 4, "hengjiang");
        Add("gao-lan", "高览", "qun", 4, "xiying");
        Add("shen-pei", "审配", "qun", 3, "gangzhi", "beizhan");
        Add("zhang-he", "张郃", "qun", 4, "yuanlve");
        Add("tian-feng", "田丰", "qun", 3, "sijian", "suishi");
        builder.AddGeneral(new("guandu:xun-chen", "荀谌", "ol-xun-chen", "ol:fenglve", "qun", 3, ["guandu:moushi"])
        { CharacterId = "character:xun-chen", VariantId = "guandu-2023", RulesetId = "guandu-2023" });
        foreach (var (id, kind) in new (string, CardKind)[]
        {
            ("intoxication", CardKind.Intoxication), ("store-grain", CardKind.StoreGrain),
            ("reinforcements", CardKind.Reinforcements), ("united-effort", CardKind.UnitedEffort),
            ("equipment-thunder-chariot", CardKind.ThunderChariot)
        })
        {
            var card = CardCatalog.Get(kind);
            builder.AddCard(new("guandu:" + id, card.DisplayName, card.CategoryName, card.Description, kind));
        }
        string[] yuan = ["yuan-shao", "xu-you", "zhang-he", "gao-lan", "chen-lin", "yan-liang-wen-chou", "ju-shou", "chun-yu-qiong", "guo-tu-feng-ji", "shen-pei", "liu-bei", "tian-feng", "xun-chen", "yuan-tan-yuan-shang", "lv-kuang-lv-xiang", "xin-pi", "han-meng", "xin-ping", "gao-gan"];
        string[] cao = ["cao-cao", "xun-yu", "xun-you", "guo-jia", "xu-huang", "yu-jin", "zhang-liao", "guan-yu", "han-hao-shi-huan", "cao-ren", "cao-hong", "cheng-yu", "liu-ye", "zang-ba", "man-chong", "li-tong", "zhang-xiu", "jia-xu", "xiahou-yuan"];
        var pools = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["team:cao"] = cao.Select(id => "guandu:" + id).ToArray(),
            ["team:yuan"] = yuan.Select(id => "guandu:" + id).ToArray()
        };
        var commanders = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["team:cao"] = "guandu:cao-cao", ["team:yuan"] = "guandu:yuan-shao" };
        string[] Layout(params int[] values) => values.Select(v => v == 0 ? "team:cao" : "team:yuan").ToArray();
        IReadOnlyList<string>[] layouts =
        [Layout(0,0,1,1,1,1,0,0), Layout(0,1,0,1,1,0,1,0), Layout(0,1,1,0,0,1,1,0), Layout(0,1,1,1,0,0,0,1), Layout(0,1,0,0,1,0,1,1)];
        builder.AddMode(new(ModeId, "官渡之战·OL2023", 8, 8, new Dictionary<string, int>(),
            "classic:standard-deck", 6, pools.Values.SelectMany(ids => ids).ToArray(), ContentModeKind.Team,
            new Dictionary<string, int> { ["team:cao"] = 4, ["team:yuan"] = 4 })
        { GuanduActivity = new(pools, commanders, Enum.GetValues<GuanduBattleEvent>(), layouts) });

        void Add(string family, string name, string faction, int hp, params string[] skills) => builder.AddGeneral(new(
            "guandu:" + family, name, family.Replace('-', '_'), "guandu:" + skills[0], faction, hp,
            skills.Skip(1).Select(id => "guandu:" + id).ToArray())
        { CharacterId = "character:" + family, VariantId = "guandu-2023", RulesetId = "guandu-2023" });
    }
}
