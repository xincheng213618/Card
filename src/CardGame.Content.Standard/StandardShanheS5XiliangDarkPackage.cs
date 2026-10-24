using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete three-enemy base roster of S5 battle/group 2663.</summary>
public sealed class StandardShanheS5XiliangDarkPackage : IGameContentPackage
{
    public const string QinyiId = "shanhe:s5-qin-yi-2663";
    public const string ChenweiId = "shanhe:s5-chen-wei-2663";
    public const string LiheiId = "shanhe:s5-li-hei-2663";
    public const string YimieId = "shanhe:s5-yimie-7005";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-xiliang-dark-boss", CurrentVersion,
        [new("standard", StandardContentPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-yimie");
        // Main battle/group 2663 西凉暗军: original order and raw 3/4, D2/H4/S1/Armor0.
        // Qin Yi skin1000413, boss Chen Wei skin1000121, Li Hei skin1000414.
        // Only base Yimie7005; Hard91/Nightmare295/Inferno1238 are excluded.
        // No fixed cards or tactics. These generic bodies have no named local portraits.
        Add(QinyiId, "秦谊", "1000413"); Add(ChenweiId, "陈卫", "1000121"); Add(LiheiId, "李黑", "1000414");
        void Add(string id, string name, string skin) => builder.AddGeneral(new(id, name, "supporter", YimieId, "qun", 4)
        { InitialHp = 3, CharacterId = "character:" + id, VariantId = "shanhe-s5-config-2663-skin-" + skin, RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male });
    }
}
