namespace CardGame.Core;

/// <summary>Facts about the printed name, type and rules text (excluding flavour text).</summary>
[Flags]
public enum PrintedCardTerms { None = 0, Slash = 1, Hp = 2, Weapon = 4, Play = 8, Distance = 16 }

public static class CardPrintedInfo
{
    // This is an explicit face-information table, never a search over engine or
    // skill descriptions. Sources and the standard-deck word list are recorded
    // in anniversary-shen-xu-chu.md and anniversary-shen-zhang-fei.md.
    private static readonly IReadOnlyDictionary<CardKind, PrintedCardTerms> Faces = Build();
    public static PrintedCardTerms Terms(CardKind kind) => Faces.GetValueOrDefault(kind);
    public static bool Contains(CardKind kind, PrintedCardTerms term) => (Terms(kind) & term) == term;
    public static IReadOnlyList<CardKind> SlashKinds { get; } = Array.AsReadOnly(
        Faces.Where(p => (p.Value & PrintedCardTerms.Slash) != 0).Select(p => p.Key).Order().ToArray());

    private static IReadOnlyDictionary<CardKind, PrintedCardTerms> Build()
    {
        var result = Enum.GetValues<CardKind>().ToDictionary(k => k, _ => PrintedCardTerms.None);
        void Add(PrintedCardTerms term, params CardKind[] kinds)
        { foreach (var kind in kinds) result[kind] |= term; }
        Add(PrintedCardTerms.Slash, CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
            CardKind.Dodge, CardKind.Alcohol, CardKind.Duel, CardKind.BarbarianAssault, CardKind.BorrowedSword,
            CardKind.Crossbow, CardKind.QinggangSword, CardKind.StoneAxe, CardKind.ZhangbaSerpentSpear,
            CardKind.CixiongDoubleSwords, CardKind.QinglongCrescentBlade, CardKind.IceSword, CardKind.QilinBow,
            CardKind.FangtianHalberd, CardKind.GudingBlade, CardKind.ZhuqueFan, CardKind.RenwangShield, CardKind.Tengjia,
            CardKind.GhostDragonCrescentBlade, CardKind.ScarletBloodSword,
            CardKind.RedBloodBlade, CardKind.Xuanjian, CardKind.WushuangHalberd, CardKind.QimenEightTrigrams,
            CardKind.ShuraInfernoHalberd, CardKind.TransplantWood, CardKind.WenheChaos, CardKind.RealgarWine,
            CardKind.QizhengXiangsheng, CardKind.QiBaoDao, CardKind.BaiBiDao);
        Add(PrintedCardTerms.Hp, CardKind.Peach, CardKind.Alcohol, CardKind.PeachGarden, CardKind.SilverLion,
            CardKind.Zong, CardKind.Reinforcements, CardKind.StarShift, CardKind.WenheChaos, CardKind.Poison,
            CardKind.SowBeans, CardKind.ShuraInfernoHalberd, CardKind.AllianceFeast, CardKind.ZhanhunDao);
        Add(PrintedCardTerms.Weapon, CardKind.Crossbow, CardKind.QinggangSword, CardKind.StoneAxe,
            CardKind.ZhangbaSerpentSpear, CardKind.CixiongDoubleSwords, CardKind.QinglongCrescentBlade,
            CardKind.IceSword, CardKind.QilinBow, CardKind.FangtianHalberd, CardKind.GudingBlade,
            CardKind.ZhuqueFan, CardKind.BorrowedSword, CardKind.GhostDragonCrescentBlade,
            CardKind.ScarletBloodSword, CardKind.XingtianAxe, CardKind.RedBloodBlade, CardKind.GeneralWeapon,
            CardKind.Xuanjian, CardKind.WushuangHalberd, CardKind.CrimsonSoulZither,
            CardKind.ShuraInfernoHalberd, CardKind.ThunderChariot, CardKind.QiBaoDao, CardKind.BaiBiDao, CardKind.ZhanhunDao);
        Add(PrintedCardTerms.Play, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.Duel,
            CardKind.BaguaFormation, CardKind.ZhangbaSerpentSpear, CardKind.QizhengXiangsheng,
            CardKind.ScarletBloodSword, CardKind.XingtianAxe, CardKind.WoodenOx);
        Add(PrintedCardTerms.Distance, CardKind.OffensiveHorse, CardKind.DefensiveHorse,
            CardKind.Dawan, CardKind.Zixing, CardKind.Dilu, CardKind.Zhaohuangfeidian, CardKind.Hualiu,
            CardKind.Snatch, CardKind.SupplyShortage, CardKind.DustlessGoldenSpear, CardKind.WenheChaos);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<CardKind, PrintedCardTerms>(result);
    }
}
