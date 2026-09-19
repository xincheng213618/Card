namespace CardGame.Core;

/// <summary>
/// Player-facing and AI-facing metadata for a card kind that the current rules
/// engine already understands. New trick/equipment kinds must not be added here
/// until the corresponding typed resolution entry points exist.
/// </summary>
public sealed record CardDefinition(
    CardKind Kind,
    string DisplayName,
    string CategoryName,
    string Description,
    int AiPlayValue,
    int AiResponseValue,
    int HandKeepValue);

public static class CardCatalog
{
    private static readonly IReadOnlyDictionary<CardKind, CardDefinition> Definitions =
        new Dictionary<CardKind, CardDefinition>
        {
            [CardKind.Slash] = new(
                CardKind.Slash,
                "杀",
                "基本牌",
                "选择一名角色；其未打出闪则受到 1 点伤害。",
                AiPlayValue: 0,
                AiResponseValue: 0,
                HandKeepValue: 40),
            [CardKind.FireSlash] = new(
                CardKind.FireSlash,
                "火杀",
                "基本牌",
                "选择一名角色；其未打出闪则受到 1 点火焰伤害。",
                AiPlayValue: 0,
                AiResponseValue: 0,
                HandKeepValue: 42),
            [CardKind.ThunderSlash] = new(
                CardKind.ThunderSlash,
                "雷杀",
                "基本牌",
                "选择一名角色；其未打出闪则受到 1 点雷电伤害。",
                AiPlayValue: 0,
                AiResponseValue: 0,
                HandKeepValue: 42),
            [CardKind.Alcohol] = new(
                CardKind.Alcohol,
                "酒",
                "基本牌",
                "出牌阶段使用使本回合下一张杀伤害 +1；濒死时可救援一名角色回复 1 点体力。",
                AiPlayValue: 18,
                AiResponseValue: 0,
                HandKeepValue: 35),
            [CardKind.Dodge] = new(
                CardKind.Dodge,
                "闪",
                "基本牌",
                "受到杀、火杀或雷杀时打出，抵消这次伤害。",
                AiPlayValue: 0,
                AiResponseValue: 65,
                HandKeepValue: 70),
            [CardKind.Peach] = new(
                CardKind.Peach,
                "桃",
                "基本牌",
                "出牌阶段回复自己 1 点体力；濒死时可救援一名角色。",
                AiPlayValue: 38,
                AiResponseValue: 0,
                HandKeepValue: 50),
            [CardKind.Duel] = new(
                CardKind.Duel,
                "决斗",
                "锦囊牌",
                "选择一名角色；双方交替打出杀，未打出者受到 1 点伤害。",
                AiPlayValue: 28,
                AiResponseValue: 0,
                HandKeepValue: 45),
            [CardKind.DrawTwo] = new(
                CardKind.DrawTwo,
                "无中生有",
                "锦囊牌",
                "出牌阶段使用，摸两张牌。",
                AiPlayValue: 32,
                AiResponseValue: 0,
                HandKeepValue: 38),
            [CardKind.BarbarianAssault] = new(
                CardKind.BarbarianAssault,
                "南蛮入侵",
                "锦囊牌",
                "所有其他角色依次打出杀，否则受到 1 点伤害。",
                AiPlayValue: 24,
                AiResponseValue: 0,
                HandKeepValue: 42),
            [CardKind.ArrowBarrage] = new(
                CardKind.ArrowBarrage,
                "万箭齐发",
                "锦囊牌",
                "所有其他角色依次打出闪，否则受到 1 点伤害。",
                AiPlayValue: 24,
                AiResponseValue: 0,
                HandKeepValue: 42),
            [CardKind.PeachGarden] = new(
                CardKind.PeachGarden,
                "桃园结义",
                "锦囊牌",
                "所有存活角色各回复 1 点体力。",
                AiPlayValue: 30,
                AiResponseValue: 0,
                HandKeepValue: 40),
            [CardKind.FiveGrains] = new(
                CardKind.FiveGrains,
                "五谷丰登",
                "锦囊牌",
                "公开展示牌堆顶牌；所有存活角色按座次各选择一张，余牌弃置。",
                AiPlayValue: 36,
                AiResponseValue: 0,
                HandKeepValue: 46),
            [CardKind.Dismantlement] = new(
                CardKind.Dismantlement,
                "过河拆桥",
                "锦囊牌",
                "选择一名其他角色；盲弃置其一张手牌，或弃置其一张公开装备/判定区牌。",
                AiPlayValue: 26,
                AiResponseValue: 0,
                HandKeepValue: 44),
            [CardKind.Snatch] = new(
                CardKind.Snatch,
                "顺手牵羊",
                "锦囊牌",
                "选择一名距离为 1 的其他角色；盲取其一张手牌，或获得其一张公开装备/判定区牌。",
                AiPlayValue: 30,
                AiResponseValue: 0,
                HandKeepValue: 42),
            [CardKind.FireAttack] = new(
                CardKind.FireAttack,
                "火攻",
                "锦囊牌",
                "选择一名有手牌的角色；其展示一张手牌，你弃置一张相同花色的手牌后对其造成 1 点火焰伤害。",
                AiPlayValue: 34,
                AiResponseValue: 0,
                HandKeepValue: 44),
            [CardKind.BorrowedSword] = new(
                CardKind.BorrowedSword,
                "借刀杀人",
                "锦囊牌",
                "选择一名装备武器的其他角色及其攻击范围内的另一名角色；前者需对后者使用一张杀，否则将武器交给你。",
                AiPlayValue: 32,
                AiResponseValue: 0,
                HandKeepValue: 45),
            [CardKind.Crossbow] = new(
                CardKind.Crossbow,
                "诸葛连弩",
                "装备牌",
                "装备至武器槽；攻击范围 1，出牌阶段可使用任意数量的杀。",
                AiPlayValue: 30,
                AiResponseValue: 0,
                HandKeepValue: 36),
            [CardKind.BaguaFormation] = new(
                CardKind.BaguaFormation,
                "八卦阵",
                "装备牌",
                "装备至防具槽；每当需要使用或打出闪时，可进行一次判定，红色判定牌视为打出闪。",
                AiPlayValue: 16,
                AiResponseValue: 0,
                HandKeepValue: 32),
            [CardKind.RenwangShield] = new(
                CardKind.RenwangShield,
                "仁王盾",
                "装备牌",
                "装备至防具槽；黑色杀对你无效。",
                AiPlayValue: 22,
                AiResponseValue: 0,
                HandKeepValue: 36),
            [CardKind.OffensiveHorse] = new(
                CardKind.OffensiveHorse,
                "赤兔",
                "装备牌",
                "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。",
                AiPlayValue: 24,
                AiResponseValue: 0,
                HandKeepValue: 34),
            [CardKind.DefensiveHorse] = new(
                CardKind.DefensiveHorse,
                "绝影",
                "装备牌",
                "装备至防御坐骑槽；其他角色到你的战斗距离 +1。",
                AiPlayValue: 24,
                AiResponseValue: 0,
                HandKeepValue: 34),
            [CardKind.JadeSeal] = new(
                CardKind.JadeSeal,
                "玉玺",
                "装备牌",
                "装备至宝物槽；摸牌阶段额外摸一张牌。",
                AiPlayValue: 26,
                AiResponseValue: 0,
                HandKeepValue: 38),
            [CardKind.QinggangSword] = new(
                CardKind.QinggangSword,
                "青釭剑",
                "装备牌",
                "装备至武器槽；攻击范围 2，你使用杀指定目标后无视其防具。",
                AiPlayValue: 32,
                AiResponseValue: 0,
                HandKeepValue: 38),
            [CardKind.StoneAxe] = new(
                CardKind.StoneAxe,
                "贯石斧",
                "装备牌",
                "装备至武器槽；攻击范围 3，当你的杀被闪抵消后，你可以弃置两张牌，令此杀仍造成伤害。",
                AiPlayValue: 36,
                AiResponseValue: 0,
                HandKeepValue: 40),
            [CardKind.Nullification] = new(
                CardKind.Nullification,
                "无懈可击",
                "锦囊牌",
                "抵消一张锦囊牌的效果；无懈之间可以继续互相抵消。",
                AiPlayValue: 0,
                AiResponseValue: 72,
                HandKeepValue: 52),
            [CardKind.IronChain] = new(
                CardKind.IronChain,
                "铁索连环",
                "锦囊牌",
                "横置或重置一至两名其他存活角色；被火焰或雷电伤害时，连环角色会传导同量伤害。",
                AiPlayValue: 25,
                AiResponseValue: 0,
                HandKeepValue: 48),
            [CardKind.Indulgence] = new(
                CardKind.Indulgence,
                "乐不思蜀",
                "锦囊牌",
                "选择一名其他角色；其下个回合判定，若结果不为红桃则跳过出牌阶段。",
                AiPlayValue: 34,
                AiResponseValue: 0,
                HandKeepValue: 46),
            [CardKind.SupplyShortage] = new(
                CardKind.SupplyShortage,
                "兵粮寸断",
                "锦囊牌",
                "选择一名有手牌的其他角色；其下个回合判定，若结果不为梅花则跳过摸牌阶段。",
                AiPlayValue: 32,
                AiResponseValue: 0,
                HandKeepValue: 44),
            [CardKind.Lightning] = new(
                CardKind.Lightning,
                "闪电",
                "锦囊牌",
                "置于自己的判定区；下个回合判定为黑桃 2 至 9 时受到 3 点雷电伤害，否则移至下一名存活角色的判定区。",
                AiPlayValue: 20,
                AiResponseValue: 0,
                HandKeepValue: 48)
        };

    public static IReadOnlyList<CardDefinition> ImplementedCards { get; } =
        Definitions.Values.OrderBy(definition => definition.Kind).ToArray();

    public static CardDefinition Get(CardKind kind) =>
        Definitions.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No card content is registered for this kind.");
}

public sealed record DeckCardCount(CardKind Kind, int Count);

/// <summary>
/// Content-only setup values for a deck. The state machine consumes these values;
/// it does not decide which card content belongs in the deck.
/// </summary>
public sealed record DeckDefinition(
    string Id,
    string Name,
    int InitialHandSize,
    int DrawPerTurn,
    IReadOnlyList<DeckCardCount> CardCounts)
{
    public int TotalCards => CardCounts.Sum(entry => entry.Count);
}

public static class StandardDeckCatalog
{
    /// <summary>
    /// The first content slice keeps a 90-card demo balance while adding Duel,
    /// DrawTwo, BarbarianAssault, ArrowBarrage, PeachGarden and FiveGrains cards
    /// to exercise immediate, response, multi-target, recovery, public-draft,
    /// hidden-target-discard, hidden-target-take, fire-attack private/public
    /// selection, elemental damage, a one-shot Slash damage boost resolution,
    /// and the five public equipment slots, including Qinggang's armor bypass,
    /// plus a bounded multi-layer Nullification response window for trick cards,
    /// public IronChain state/elemental propagation, two phase-skipping delayed
    /// judgment cards, and Lightning's public judgment/transfer/damage lifecycle.
    /// </summary>
    public static DeckDefinition BasicDemo { get; } = new(
        Id: "basic-demo",
        Name: "基础牌演示牌堆",
        InitialHandSize: 4,
        DrawPerTurn: 2,
        CardCounts:
        [
            new DeckCardCount(CardKind.Slash, 18),
            new DeckCardCount(CardKind.Dodge, 18),
            new DeckCardCount(CardKind.Peach, 10),
            new DeckCardCount(CardKind.Duel, 4),
            new DeckCardCount(CardKind.DrawTwo, 2),
            new DeckCardCount(CardKind.BarbarianAssault, 2),
            new DeckCardCount(CardKind.ArrowBarrage, 2),
            new DeckCardCount(CardKind.PeachGarden, 2),
            new DeckCardCount(CardKind.FiveGrains, 2),
            new DeckCardCount(CardKind.Dismantlement, 2),
            new DeckCardCount(CardKind.Snatch, 2),
            new DeckCardCount(CardKind.FireSlash, 2),
            new DeckCardCount(CardKind.ThunderSlash, 2),
            new DeckCardCount(CardKind.Alcohol, 2),
            new DeckCardCount(CardKind.FireAttack, 2),
            new DeckCardCount(CardKind.Crossbow, 2),
            new DeckCardCount(CardKind.BaguaFormation, 1),
            new DeckCardCount(CardKind.OffensiveHorse, 1),
            new DeckCardCount(CardKind.DefensiveHorse, 1),
            new DeckCardCount(CardKind.JadeSeal, 1),
            new DeckCardCount(CardKind.QinggangSword, 1),
            new DeckCardCount(CardKind.Nullification, 2),
            new DeckCardCount(CardKind.IronChain, 2),
            new DeckCardCount(CardKind.Indulgence, 2),
            new DeckCardCount(CardKind.SupplyShortage, 2),
            new DeckCardCount(CardKind.Lightning, 2),
            new DeckCardCount(CardKind.RenwangShield, 1)
        ]);

    public static IReadOnlyList<Card> CreateBasicDemoDeck() => CreateDeck(BasicDemo);

    public static IReadOnlyList<Card> CreateDeck(DeckDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Validate(definition);

        var cards = new List<Card>(definition.TotalCards);
        var id = 1;
        foreach (var entry in definition.CardCounts)
        {
            for (var copy = 0; copy < entry.Count; copy++)
            {
                var suit = (Suit)((id - 1) % 4);
                var rank = ((id - 1) % 13) + 1;
                cards.Add(new Card(id++, entry.Kind, suit, rank));
            }
        }

        return cards;
    }

    private static void Validate(DeckDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new ArgumentException("A deck definition must have an id.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("A deck definition must have a name.", nameof(definition));
        }

        if (definition.InitialHandSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition.InitialHandSize));
        }

        if (definition.DrawPerTurn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition.DrawPerTurn));
        }

        if (definition.CardCounts.Count == 0 || definition.CardCounts.Any(entry => entry.Count <= 0))
        {
            throw new ArgumentException("A deck definition must contain positive card counts.", nameof(definition));
        }

        if (definition.CardCounts.Select(entry => entry.Kind).Distinct().Count() != definition.CardCounts.Count)
        {
            throw new ArgumentException("A deck definition cannot repeat a card kind in its count list.", nameof(definition));
        }

        foreach (var entry in definition.CardCounts)
        {
            CardCatalog.Get(entry.Kind);
        }
    }
}
