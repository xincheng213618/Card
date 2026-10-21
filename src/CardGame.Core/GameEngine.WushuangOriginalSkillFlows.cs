namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ContinueWushuangYiji(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        {
            _ = EnsureDrawPile();
            var cards = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Take(2).ToArray();
            f = UpdateWushuangOriginal(f, r with { Stage = "yiji-give", Cards = cards.Select(c => c.Id).ToArray() });
            foreach (var card in cards) MoveCard(card, CardLocation.DrawPile, CardLocation.Processing,
                new("skill-program.wushuang.yiji.view"), disclosure: CardMovementDisclosure.FaceDown);
            SetProgramCardSet(f.Id, "wushuang-yiji", cards.Select(c => c.Id).ToArray(), SkillProgramCardSetVisibility.Private,
                cards.Select(_ => CardLocation.Processing).ToArray());
            RunWushuangOriginalSkill(f.Id); return;
        }
        var remaining = r.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
        if (remaining.Length == 0) { FinishWushuangOriginal(f); return; }
        var recipients = _players.Where(p => p.IsAlive).ToArray();
        if (!_players[f.OwnerSeat].IsAlive)
        {
            foreach (var id in remaining) MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DiscardPile,
                new("skill-program.wushuang.yiji.cleanup"));
            FinishWushuangOriginal(f); return;
        }
        PublishWushuangOriginalChoice(f, remaining.SelectMany(id => recipients.Select(p =>
            WushuangOriginalChoice(f, "yiji-give", $"将【{GetAdvancedCard(id).DisplayName}】交给 {p.Name}", [id], [p.Seat]))).ToArray());
    }
    private void ContinueWushuangJieming(ProgramSkillFrame f)
    {
        PublishWushuangOriginalChoice(f, _players.Where(p => p.IsAlive && GetHand(p).Count < Math.Min(5, p.MaxHp))
            .Select(p => WushuangOriginalChoice(f, "jieming-target", $"令 {p.Name} 将手牌摸至 {Math.Min(5, p.MaxHp)} 张", targets: [p.Seat])).ToArray());
    }
    private void ContinueWushuangYashi(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage != "begin") { ContinueWushuangGuanxu(f); return; }
        var choices = WushuangOtherTargets(f, "guanxu-target", p => GetHand(p).Count > 0).ToList();
        if (f.WindowContext?.SourceSeat is { } source && _players[source].IsAlive)
            choices.Insert(0, WushuangOriginalChoice(f, "yashi-suppress", $"令 {_players[source].Name} 的非锁定技失效至其下回合开始", targets: [source]));
        PublishWushuangOriginalChoice(f, choices);
    }
    private void ContinueWushuangGuanxu(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        { PublishWushuangOriginalChoice(f, WushuangOtherTargets(f, "guanxu-target", p => GetHand(p).Count > 0)); return; }
        if (r.Stage == "guanxu-view")
        {
            var hand = r.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(r.Target));
            var top = r.Top.Where(id => _cardZones.GetLocation(id) == CardLocation.DrawPile);
            PublishWushuangOriginalChoice(f, hand.SelectMany(h => top.Select(d =>
                WushuangOriginalChoice(f, "guanxu-exchange", $"以手牌【{GetAdvancedCard(h).DisplayName}】交换牌堆【{GetAdvancedCard(d).DisplayName}】", [h, d], [r.Target])))
                .Append(WushuangOriginalChoice(f, "finish", "观看完毕，不交换")).ToArray()); return;
        }
        if (r.Stage != "guanxu-discard") throw new InvalidOperationException("Guanxu lost its exact exchange.");
        var triples = GetHand(_players[r.Target]).Where(c => !IsSelfHandCategoryDiscardForbidden(r.Target, c, CardLocation.Hand(r.Target), OwnedCardMoveIntent.Discard))
            .GroupBy(c => EffectiveSuit(_players[r.Target], c)).SelectMany(group => EnumerateCardCombinations(group.ToArray(), 3));
        PublishWushuangOriginalChoice(f, triples.Select(cards => WushuangOriginalChoice(f, "guanxu-discard",
            "弃置三张同花色手牌：" + string.Join('、', cards.Select(c => c.DisplayName)), cards.Select(c => c.Id).ToArray(), [r.Target])).ToArray());
    }
    private void ContinueWushuangChoufa(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        { PublishWushuangOriginalChoice(f, WushuangOtherTargets(f, "choufa-target", p => GetHand(p).Count > 0)); return; }
        // These choices deliberately disclose positions, never the unseen names.
        var hand = GetHand(_players[r.Target]).ToArray();
        PublishWushuangOriginalChoice(f, hand.Select((card, i) => WushuangOriginalChoice(f, "choufa-reveal",
            $"展示 {_players[r.Target].Name} 的第 {i + 1} 张手牌", targets: [r.Target]))
            .Select((choice, index) => choice with { Parameters = new Dictionary<string, string>(choice.Parameters) { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                Id = new(choice.Id.Value + "." + index) }).ToArray());
    }
    private void ContinueWushuangShenfu(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
            f = UpdateWushuangOriginal(f, r = r with { Stage = GetHand(_players[f.OwnerSeat]).Count % 2 == 1 ? "shenfu-odd" : "shenfu-even" });
        if (r.Stage == "shenfu-odd-after")
        {
            if (_players[r.Target].IsAlive) { FinishWushuangOriginal(f); return; }
            f = WushuangOriginalStage(f, "shenfu-odd"); r = f.WushuangOriginalSkill!;
        }
        if (r.Stage == "shenfu-even-after")
        {
            if (!_players[r.Target].IsAlive || GetHand(_players[r.Target]).Count != _players[r.Target].Hp)
            { FinishWushuangOriginal(f); return; }
            f = WushuangOriginalStage(f, "shenfu-even"); r = f.WushuangOriginalSkill!;
        }
        if (!_players[f.OwnerSeat].IsAlive) { FinishWushuangOriginal(f); return; }
        if (r.Stage == "shenfu-odd")
        {
            var oddChoices = WushuangOtherTargets(f, "shenfu-damage").ToList();
            if (r.Count > 0) oddChoices.Add(WushuangOriginalChoice(f, "finish", "不再造成雷电伤害"));
            PublishWushuangOriginalChoice(f, oddChoices); return;
        }
        if (r.Stage != "shenfu-even") throw new InvalidOperationException("Shenfu lost its fixed parity branch.");
        var choices = _players.Where(p => p.IsAlive).Select(p => WushuangOriginalChoice(f, "shenfu-draw", $"令 {p.Name} 摸一张牌", targets: [p.Seat])).ToList();
        choices.AddRange(_players.Where(p => p.IsAlive && GetHand(p).Count > 0).Select(p =>
            WushuangOriginalChoice(f, "shenfu-discard-target", $"弃置 {p.Name} 一张手牌", targets: [p.Seat])));
        if (r.Count > 0) choices.Add(WushuangOriginalChoice(f, "finish", "结束神赋")); PublishWushuangOriginalChoice(f, choices);
    }
    private void ContinueWushuangLingren(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        {
            var use = LifecycleCardUse(f.WindowContext!.CardUse!.ParentCardUseFrameId)!;
            PublishWushuangOriginalChoice(f, use.TargetSeats.Where(t => _players[t].IsAlive).Distinct().Select(t =>
                WushuangOriginalChoice(f, "lingren-target", $"猜测 {_players[t].Name} 的手牌类型", targets: [t])).ToArray()); return;
        }
        if (r.Stage != "lingren-guess") throw new InvalidOperationException("Lingren lost its frozen target categories.");
        PublishWushuangOriginalChoice(f, Enumerable.Range(0, 8).Select(mask => WushuangOriginalChoice(f, "lingren-guess-" + mask,
            $"基本牌：{((mask & 1) != 0 ? "有" : "无")}；锦囊牌：{((mask & 2) != 0 ? "有" : "无")}；装备牌：{((mask & 4) != 0 ? "有" : "无")}")).ToArray());
    }
    private void ContinueWushuangTairan(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        {
            var owner = _players[f.OwnerSeat];
            f = UpdateWushuangOriginal(f, r with { Stage = "tairan-recovered", Count = Math.Max(0, owner.MaxHp - owner.Hp), Index = owner.Hp });
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, Math.Max(0, owner.MaxHp - owner.Hp), null, null);
            RunWushuangOriginalSkill(f.Id); return;
        }
        if (r.Stage != "tairan-recovered") throw new InvalidOperationException("Tairan lost its once-recovered debt.");
        if (!ProgramEventHistory<WushuangTairanHealedEvent>().Any(e => e.FrameId == f.Id))
            AdvanceEventRulesAndQueueFact(new WushuangTairanHealedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, _turnNumber, 0));
        DrawWushuangOriginal(f, f.OwnerSeat, Math.Max(0, _players[f.OwnerSeat].MaxHp - GetHand(_players[f.OwnerSeat]).Count), "finish");
    }
    private void ContinueWushuangTairanSettlement(ProgramSkillFrame f)
    {
        var r = f.WushuangOriginalSkill!;
        if (r.Stage == "begin")
        {
            var settled = ProgramEventHistory<WushuangTairanSettledEvent>().Select(e => e.ProducerId).ToHashSet();
            var healing = ProgramEventHistory<WushuangTairanHealedEvent>().Where(e => e.Owner == f.OwnerSeat && !settled.Contains(e.FrameId)).ToArray();
            var drawn = ProgramEventHistory<WushuangTairanDrawnEvent>().Where(e => e.Owner == f.OwnerSeat && !settled.Contains(e.FrameId)).ToArray();
            foreach (var id in healing.Select(e => e.FrameId).Concat(drawn.Select(e => e.FrameId)).Distinct())
                AdvanceEventRulesAndQueueFact(new WushuangTairanSettledEvent(id, f.OwnerSeat, f.Id));
            f = UpdateWushuangOriginal(f, r with { Stage = "tairan-discard", Count = healing.Sum(e => e.Recovered),
                Cards = drawn.SelectMany(e => e.CardIds).Distinct().ToArray() });
            if (f.WushuangOriginalSkill!.Count > 0)
            { new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, f.WushuangOriginalSkill.Count); RunWushuangOriginalSkill(f.Id); return; }
            r = f.WushuangOriginalSkill;
        }
        f = WushuangOriginalStage(f, "finish");
        var held = r.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat)).ToArray();
        DiscardWushuangOriginal(f, f.OwnerSeat, held); RunWushuangOriginalSkill(f.Id);
    }
    private void DiscardWushuangOriginal(ProgramSkillFrame f, int owner, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return;
        var reason = new CardMoveReason("skill-program." + f.SkillId + ".discard");
        var invoice = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, owner, ids, reason, f.Id);
        foreach (var id in ids)
        {
            var from = _cardZones.GetLocation(id); var card = GetAdvancedCard(id);
            if (from.OwnerSeat != owner || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                IsSelfHandCategoryDiscardForbidden(owner, card, from, OwnedCardMoveIntent.Discard))
                throw new InvalidOperationException("An official skill discard lost its exact owned legal entity.");
            MoveCard(card, from, CardLocation.DiscardPile, reason, disclosure: CardMovementDisclosure.FaceUp);
        }
        CompleteNativeLogicalDiscardInvocation(invoice);
    }
}
