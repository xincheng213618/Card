using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The S5 group 2652 base form reuses the source-identical boundary skills.</summary>
public sealed class StandardShanheS5ZhouyuPackage : IGameContentPackage
{
    public const string ZhouyuId = "shanhe:s5-zhou-yu-2652";
    public static readonly Version CurrentVersion = new(1, 0, 0);

    public PackageManifest Manifest { get; } = new("shanhe-s5-zhouyu-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // S5 chapter 6 Yique / boss 5068 / battle and main group 2652:
        // one Zhouyu, raw 3/3, Draw 2 / hand 4 / Slash 1 / Armor 0.
        // Source skills 712/713 match boundary Yingzi/Fanjian; the Draw 2
        // base does not include Yingzi. Hard card 20641 and nightmare card
        // 20636 are excluded; the base form has no fixed cards or tactics.
        // Wu/Male and the boundary portrait are explicit local mappings.
        builder.AddGeneral(new(ZhouyuId, "周瑜", "boundary_zhou_yu", "boundary:yingzi", "wu", 3, ["boundary:fanjian"])
        {
            InitialHp = 3, CharacterId = "character:zhou-yu", VariantId = "shanhe-s5-config-2652",
            RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
        });
    }
}
