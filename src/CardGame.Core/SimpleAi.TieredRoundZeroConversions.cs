namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private (double Score, string Reason) ScoreTieredRoundZeroAction(GameSnapshot view, PlayerSnapshot self,
        Role role, LegalAction action)
    {
        if (action.CardId != 0 || action.TieredRoundZeroUse is null || action.ConversionSource is null ||
            action.PlayedCardKind is not { } kind)
            return (-1000d, "零材料动作缺少已发布的转换政策。");
        var profile = CardCatalog.Get(kind);
        const double roundCost = 4d;
        if (action.Kind == LegalActionKind.Peach)
            return ((self.MaxHp - self.Hp) * profile.AiPlayValue + (self.Hp <= 1 ? 50 : 0) - roundCost,
                "使用本轮额度回复自己的公开体力；没有虚构实体材料。");
        if (action.Kind == LegalActionKind.DrawTwo)
            return (profile.AiPlayValue + Math.Min(2, view.DrawPileCount + view.DiscardPileCount) * 4 - roundCost,
                "用本轮额度换取真实摸牌，只读取公开牌堆数量。");
        if (action.Kind == LegalActionKind.Alcohol)
        {
            var slashes = self.Hand.Count(c => IsSlashCard(c.Kind) ||
                self.Skills?.Any(s => CanViewAs(s, c, CardKind.Slash, forPlay: true)) == true);
            return (slashes == 0 ? -10 : profile.AiPlayValue + Math.Min(slashes, 2) * 10 - roundCost,
                "按自己可见的后续实体杀评估酒，不把已消费的本轮额度再计作一张杀。");
        }
        if (action.Kind == LegalActionKind.PeachGarden)
            return (profile.AiPlayValue + view.Players.Where(p => p.IsAlive && p.Hp < p.MaxHp)
                .Sum(p => p.Seat == self.Seat ? 24d : -GetHostility(view, role, p) * .12d) - roundCost,
                "按公开阵营和受伤人数评估群体回复。");
        if (action.Kind == LegalActionKind.FiveGrains)
            return (profile.AiPlayValue + view.Players.Count(p => p.IsAlive) * 4 - roundCost,
                "按公开存活人数评估选牌，不读取牌堆顺序。");
        if (action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage)
            return (profile.AiPlayValue + view.Players.Where(p => p.IsAlive && p.Seat != self.Seat)
                .Sum(p => GetHostility(view, role, p) * .12d + (p.Hp <= 1 ? 8 : 0)) - roundCost,
                "按公开阵营、体力和原目标评估群体伤害。");
        if (action.Kind == LegalActionKind.IronChain)
            return (profile.AiPlayValue + action.TargetSeats.Select(seat => view.Players.Single(p => p.Seat == seat))
                .Sum(p => (p.IsChained ? -1 : 1) * (p.Seat == self.Seat ? -95 : GetHostility(view, role, p))) - roundCost,
                "按公开连环标记及已发布合法目标评估。");
        if (action.Kind == LegalActionKind.BorrowedSword)
        {
            if (action.TargetSeats.Count != 2) return (-1000, "借刀缺少原有序目标。");
            var armed = view.Players.Single(p => p.Seat == action.TargetSeats[0]);
            var victim = view.Players.Single(p => p.Seat == action.TargetSeats[1]);
            var weapon = armed.Equipment.Where(c => EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Weapon)
                .Select(c => CardCatalog.Get(c.Kind).AiPlayValue).DefaultIfEmpty(12).Max();
            return (profile.AiPlayValue + GetHostility(view, role, armed) * .45 + GetHostility(view, role, victim) * .65 +
                weapon * .35 + (victim.Hp <= 1 ? 22 : 0) - roundCost, "按公开武器和有序目标评估借刀。");
        }
        if (action.TargetSeats.Count == 0) return (-1000, "该零材料用牌没有成熟合法目标。");
        var targets = action.TargetSeats.Select(seat => view.Players.Single(p => p.Seat == seat)).ToArray();
        var pressure = targets.Sum(p => GetHostility(view, role, p) + (p.Hp <= 1 ? 28 : 0));
        if (action.Kind is LegalActionKind.Dismantlement or LegalActionKind.Snatch)
            pressure += targets.Sum(p => Math.Min(p.HandCount, 5) * 4);
        if (action.Kind == LegalActionKind.FireAttack)
            pressure += targets.Sum(p => Math.Min(p.HandCount, 5) * 5);
        if (action.Kind == LegalActionKind.Slash)
            pressure += targets.Sum(p => p.Equipment.Any(c => c.Kind == CardKind.SilverLion) && self.HasAlcoholEffect ? -36d : 0d);
        return (profile.AiPlayValue + pressure - roundCost,
            "仅按已发布原目标、公开阵营、体力、防具和手牌数量评分，不构造实体材料或读取暗牌。");
    }

    // The engine passes only a genuinely published zero-material choice. This
    // uses public relationships; neither physical cards nor hidden IDs are read.
    internal (bool Use, AiThoughtRecord Thought) ChooseTieredZeroCounterspell(GameSnapshot view, CardKind effectKind,
        int sourceSeat, IReadOnlyList<int> targets, bool nullified, int depth, int sequence)
    {
        var self = view.Players.Single(p => p.Seat == Seat); var source = view.Players.Single(p => p.Seat == sourceSeat);
        var role = self.Role ?? Role.Renegade;
        var favor = targets.Count == 0 ? ScoreNullificationFavor(view, role, source, null, effectKind) :
            targets.Sum(seat => ScoreNullificationFavor(view, role, source, view.Players.Single(p => p.Seat == seat), effectKind));
        var useScore = nullified ? favor > 0 ? 48 + favor : -12 : favor < 0 ? 54 - favor : 2;
        if (targets.Contains(Seat) && (_policyVersion == 1 || (nullified ? favor > 0 : favor < 0))) useScore += 38;
        var passScore = nullified ? favor > 0 ? -35 : 18 : favor < 0 ? -24 : 12;
        // Reserve a modest value for the real shared Round allowance, not a
        // fictitious physical material's hand keep value.
        useScore -= 4;
        var choices = new[] { new AiCandidateScore(new LegalAction(LegalActionKind.Nullification, 0, sourceSeat, "视为使用【无懈可击】"),
            useScore, "按公开阵营、原目标和锦囊效果评估，消费已发布的本轮转换额度。"),
            new AiCandidateScore(new LegalAction(LegalActionKind.SkipNullification, null, sourceSeat, "保留本轮转换额度"), passScore,
                "不读取外国手牌或隐藏材料。") };
        var selected = choices.OrderByDescending(c => c.Score).First();
        return (selected.Action.Kind == LegalActionKind.Nullification,
            new(sequence, view.TurnNumber, Seat, selected.Action.Description, choices, $"第{depth}层真实无懈使用：{selected.Action.Description}。"));
    }
}
