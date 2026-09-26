using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// The first national-war slice. It deliberately reuses the standard deck and
/// skills while giving the mode its own general ids, faction contract and
/// hidden dual-general setup. Future packages can add real national skills
/// without changing this state-machine boundary.
/// </summary>
public sealed class StandardNationalWarLitePackage : IGameContentPackage
{
    public PackageManifest Manifest { get; } = new(
        "standard-national-war-lite",
        new Version(1, 1, 0),
        [new PackageDependency("standard", new Version(1, 11, 0))]);

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddGeneral(new ContentGeneralDefinition(
            "national:wei-cao-cao", "曹操", "cao_cao", "standard:jianxiong", "wei"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:wei-guo-jia", "郭嘉", "guo_jia", "standard:yiji", "wei", BaseHp: 3));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:wei-xun-yu", "荀彧", "xun_yu", "standard:jieming", "wei", BaseHp: 3));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:wei-ganglie", "刚烈者", "ganglie", "standard:ganglie", "wei"));

        builder.AddGeneral(new ContentGeneralDefinition(
            "national:shu-zhang-fei", "张飞", "zhang_fei", "standard:paoxiao", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:shu-guan-yu", "关羽", "guan_yu", "standard:wusheng", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:shu-zhao-yun", "赵云", "zhao_yun", "standard:longdan", "shu"));
        builder.AddGeneral(new ContentGeneralDefinition(
            "national:shu-liu-bei", "刘备", "liu_bei", "standard:none", "shu"));

        builder.AddMode(new ContentModeDefinition(
            Id: "national:lite-4",
            Name: "四人国战（简化）",
            MinPlayers: 4,
            MaxPlayers: 4,
            RoleCounts: new Dictionary<string, int>(),
            DeckId: "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds:
            [
                "national:wei-cao-cao",
                "national:wei-guo-jia",
                "national:wei-xun-yu",
                "national:wei-ganglie",
                "national:shu-zhang-fei",
                "national:shu-guan-yu",
                "national:shu-zhao-yun",
                "national:shu-liu-bei"
            ],
            ModeKind: ContentModeKind.NationalWarLite,
            FactionCounts: new Dictionary<string, int>
            {
                ["wei"] = 2,
                ["shu"] = 2
            }));
    }
}
