using System.Reflection;
using System.Numerics;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current OL Zhang Changpu: docs/content/sources/ol-zhang-chang-pu-2026-10-06.json.
internal static class OrdinaryZhangChangPuChecks
{
    private const string Yanjiao = "ol:yanjiao";
    private const string Shengshen = "ol:shengshen";
    private const string Hurt = "fixture:zcp-hurt-trigger";
    private const string Order = "fixture:zcp-order";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["ol:zhang-chang-pu"];
        Require(general.Name == "张昌蒲" && general.FactionId == "wei" && general.BaseHp == 3 &&
            general.SkillIds.SequenceEqual([Yanjiao, Shengshen]) &&
            general.VariantId == "ordinary" && general.RulesetId == "sanguosha-ol",
            "The OL Zhang Changpu general registers the current wei 3HP skill pair.");
        var yanjiao = registry.GetSkill(Yanjiao).Program!.Triggers.Single();
        Require(yanjiao.Window == SkillProgramTriggerWindow.PlayPhaseStarting && yanjiao.Optional &&
            yanjiao.Effects.Select(e => e.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.YanjiaoRevealTopCards,
                SkillProgramEffectOp.YanjiaoSplitRevealedCards, SkillProgramEffectOp.MoveBoundCards,
                SkillProgramEffectOp.MoveBoundCards, SkillProgramEffectOp.MoveBoundCards,
                SkillProgramEffectOp.ChangeParticipantMarker]) &&
            yanjiao.Effects[1].Amount == 4 && yanjiao.Effects[1].Marker == PlayerMarkerKind.ShenJiao &&
            yanjiao.Effects[2].SourceBind == "revealed" && yanjiao.Effects[2].OwnerBind == "group-owner" &&
            yanjiao.Effects[2].ChooserBind == "group-chooser" && yanjiao.Effects[2].LeftoverBind == "leftover" &&
            yanjiao.Effects[3].Destination == SkillProgramCardDestination.OwnerHand &&
            yanjiao.Effects[4].Destination == SkillProgramCardDestination.SelectedTargetHand &&
            yanjiao.Effects[5].Destination == SkillProgramCardDestination.DiscardPile &&
            yanjiao.Effects[6].ClearMarker && yanjiao.Effects[6].Amount == -1 &&
            yanjiao.Effects[6].Marker == PlayerMarkerKind.ShenJiao,
            "Yanjiao reveals four-plus-bonus cards, lets the counterpart split equal-rank-sum groups, and consumes the bonus.");
        var shengshen = registry.GetSkill(Shengshen).Program!.Triggers.Single();
        Require(shengshen.Window == SkillProgramTriggerWindow.AfterDamageApplied && shengshen.Optional &&
            shengshen.Effects.Single().Op == SkillProgramEffectOp.ShenShenDrawAndArmBonus &&
            shengshen.Effects.Single().Marker == PlayerMarkerKind.ShenJiao,
            "Shengshen draws and arms the next Yanjiao reveal bonus after damage.");
        Require(registry.GetSkill(Yanjiao).ProgramPresentation!.Name == "严教" &&
            registry.GetSkill(Shengshen).ProgramPresentation!.Name == "省身",
            "The presentation carries the current OL wording.");
    }

    public static void YanjiaoSplitsEqualRankSums()
    {
        var (g, r) = Start("identity:zcp-split", "fixture:zcp");
        ArrangeTop(g, 4);
        SettleToYanjiaoPrompt(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([1]));
        Settle(g);
        var revealed = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single();
        Require(revealed.Bind == "revealed" && revealed.Cards.Count == 4,
            "Yanjiao reveals the four arranged card faces. " + string.Join(",", revealed.Cards.Select(c => c.Rank)));
        var (ownerIds, chooserIds, leftoverIds) = ExpectedAiSplit(revealed.Cards.Select(c => c.Rank).ToArray(),
            revealed.Cards.Select(c => c.Id).ToArray());
        Require(ownerIds.Length > 0, "The arranged faces admit an equal-rank-sum split.");
        var hand0 = CardsAt(g, CardLocation.Hand(0));
        var hand1 = CardsAt(g, CardLocation.Hand(1));
        Require(ownerIds.All(id => hand0.Any(c => c.Id == id)) &&
            chooserIds.All(id => hand1.Any(c => c.Id == id)) && ownerIds.Concat(chooserIds)
                .All(id => !g.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)),
            "The counterpart assigns the largest feasible equal-rank-sum group to the owner and keeps its partner. ranks=" +
                string.Join(",", revealed.Cards.Select(c => c.Rank)) + " owner=" + string.Join(",", ownerIds) +
                " chooser=" + string.Join(",", chooserIds) + " leftover=" + string.Join(",", leftoverIds) +
                " hand0=" + string.Join(",", hand0.Select(c => c.Id).OrderBy(id => id)) +
                " hand1=" + string.Join(",", hand1.Select(c => c.Id).OrderBy(id => id)));
        Require(leftoverIds.All(id => g.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)),
            "Only the ungrouped cards reach the discard pile.");
        Require(!g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
            .Any(m => m.Marker == PlayerMarkerKind.ShenJiao),
            "Without Shengshen damage the Yanjiao launch leaves no marker trail.");
        Replay(g, r);
    }

    public static void YanjiaoWithoutFeasibleSplitDiscardsAndPenalizes()
    {
        var (g, r) = Start("identity:zcp-nosplit", "fixture:zcp");
        ArrangeTop(g, 4, [1, 2, 4, 8]);
        SettleToYanjiaoPrompt(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([1]));
        Settle(g);
        var revealed = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single();
        Require(revealed.Cards.Count == 4 && revealed.Cards.Select(c => c.Rank).Order().SequenceEqual([1, 2, 4, 8]),
            "The arranged infeasible faces reach the reveal. " + string.Join(",", revealed.Cards.Select(c => c.Rank)));
        var revealedIds = revealed.Cards.Select(c => c.Id).ToHashSet();
        Require(CardsAt(g, CardLocation.Hand(0)).All(c => !revealedIds.Contains(c.Id)) &&
            CardsAt(g, CardLocation.Hand(1)).All(c => !revealedIds.Contains(c.Id)),
            "Without a feasible split neither hand gains a revealed card.");
        Require(revealedIds.All(id => g.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)),
            "All ungroupable cards reach the discard pile.");
        var handBeforeDiscard = CardsAt(g, CardLocation.Hand(0)).Count;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        SettleThroughDiscard(g);
        var limited = g.Events.Select(e => e.Payload).OfType<HandLimitDiscardedEvent>().Single();
        var hpAtDiscard = g.CreateSnapshot(0).Players[0].Hp;
        Require(handBeforeDiscard - limited.CardIds.Count == hpAtDiscard - 1,
            "More than one ungrouped card lowers this turn's hand limit by one. kept=" +
                (handBeforeDiscard - limited.CardIds.Count) + " hp=" + hpAtDiscard);
        Replay(g, r);
    }

    public static void ShengshenFeedsAndConsumesYanjiaoReveal()
    {
        var (g, r) = Start("identity:zcp-hurt", "fixture:zcp-hurt");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Hurt));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Shengshen));
        var handBefore = CardsAt(g, CardLocation.Hand(0)).Count;
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var draws = g.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value.Contains("shengshen", StringComparison.Ordinal));
        Require(draws == 2, "The fewest-hand owner draws two after the fixture damage. drew=" + draws);
        var armed = g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
            .Where(m => m.Marker == PlayerMarkerKind.ShenJiao).Single();
        Require(armed.Delta == 2 && armed.Count == 2,
            "The fewest-hp owner arms the next reveal by two. " + armed);
        SkipYanjiaoAndArrange(g, 6);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            c.Targets.SequenceEqual([1]));
        Settle(g);
        var revealed = g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Single();
        Require(revealed.Cards.Count == 6,
            "The armed bonus widens the reveal to six cards. " + string.Join(",", revealed.Cards.Select(c => c.Rank)));
        var (ownerIds, chooserIds, leftoverIds) = ExpectedAiSplit(revealed.Cards.Select(c => c.Rank).ToArray(),
            revealed.Cards.Select(c => c.Id).ToArray());
        var hand0 = CardsAt(g, CardLocation.Hand(0));
        Require(hand0.Count == handBefore + 2 + 1 + ownerIds.Length &&
            ownerIds.All(id => hand0.Any(c => c.Id == id)),
            "The AI-selected largest feasible group reaches the owner hand. hand=" + hand0.Count);
        var hand1 = CardsAt(g, CardLocation.Hand(1));
        Require(chooserIds.All(id => hand1.Any(c => c.Id == id)),
            "The counterpart keeps the equal-sum partner group.");
        Require(leftoverIds.All(id => g.CardMovements.Any(m => m.CardId == id && m.To == CardLocation.DiscardPile)),
            "The ungrouped cards reach the discard pile.");
        var markerTrail = g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
            .Where(m => m.Marker == PlayerMarkerKind.ShenJiao).ToList();
        Require(markerTrail.Last().Count == 0,
            "Yanjiao consumes the whole bonus after the reveal. " + Describe(markerTrail));
        Replay(g, r);
    }

    public static void ShengshenArmCapsAtFour()
    {
        var (g, r) = Start("identity:zcp-hurt", "fixture:zcp-hurt");
        for (var round = 0; round < 3; round++)
        {
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Hurt));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Shengshen));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            var trail = g.Events.Select(e => e.Payload).OfType<PlayerMarkerChangedEvent>()
                .Where(m => m.Marker == PlayerMarkerKind.ShenJiao).ToList();
            Require(trail.All(m => m.Count <= 4),
                "The armed bonus never exceeds four. " + Describe(trail));
            if (round == 2)
            {
                Require(trail.Last().Count == 4,
                    "Two armed damages reach the cap and the third stays at four. " + Describe(trail));
                var draws = g.CardMovements.Count(m => m.To == CardLocation.Hand(0) &&
                    m.Reason.Value.Contains("shengshen", StringComparison.Ordinal));
                Require(draws >= 6, "Each Shengshen trigger keeps drawing while the cap holds. drew=" + draws);
                break;
            }
            SkipYanjiaoAndSkipOrder(g);
        }
        Replay(g, r);
    }

    // Rearranges the draw-pile top through the fixture order trigger: the
    // first arranged card feeds the owner's one-card draw phase, the rest
    // become the exact Yanjiao reveal in answer order.
    private static void ArrangeTop(GameEngine g, int revealCount, int[]? ranks = null)
    {
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Order));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var prompt = P(g) ?? throw new InvalidOperationException("The order trigger opened no reorder prompt.");
        var viewed = prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("action") == "top")
            .Select(c => c.Cards[0]).ToArray();
        Require(viewed.Length >= revealCount + 1,
            "The shuffled top sixteen cover the arrangement. seen=" + viewed.Length);
        var chosen = ranks is null ? viewed.Take(revealCount + 1).ToArray()
            : RanksFromViewed(g, viewed, ranks);
        foreach (var cardId in chosen)
            Answer(g, c => c.Parameters.GetValueOrDefault("action") == "top" && c.Cards.FirstOrDefault() == cardId);
        Answer(g, c => c.Parameters.GetValueOrDefault("action") == "finish-top");
        foreach (var cardId in viewed.Where(id => !chosen.Contains(id)).ToArray())
            Answer(g, c => c.Parameters.GetValueOrDefault("action") == "bottom" && c.Cards.FirstOrDefault() == cardId);
    }

    private static int[] RanksFromViewed(GameEngine g, int[] viewed, int[] ranks)
    {
        var picked = new List<int>();
        var drawPile = CardsAt(g, CardLocation.DrawPile).ToDictionary(card => card.Id, card => card.Rank);
        foreach (var rank in ranks)
        {
            var match = viewed.FirstOrDefault(id => !picked.Contains(id) && drawPile.GetValueOrDefault(id) == rank);
            Require(match != 0, "The viewed top lacks a card of rank " + rank + ".");
            picked.Add(match);
        }
        return viewed.Where(id => !picked.Contains(id)).Take(1).Concat(picked).ToArray();
    }

    private static void SettleToYanjiaoPrompt(GameEngine g)
    {
        for (var i = 0; i < 300; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Yanjiao) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
                return;
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the Yanjiao prompt: " + P(g)?.Prompt);
    }

    private static void SkipYanjiaoAndArrange(GameEngine g, int revealCount)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Order))
            {
                ArrangeTop(g, revealCount);
                return;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Yanjiao) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never reached the next order prompt: " + P(g)?.Prompt);
    }

    private static void SkipYanjiaoAndSkipOrder(GameEngine g)
    {
        for (var i = 0; i < 400; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Order or Yanjiao) &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Hurt))
                return;
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else
                Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to a hurt prompt: " + P(g)?.Prompt);
    }

    private static void SettleThroughDiscard(GameEngine g)
    {
        for (var i = 0; i < 200; i++)
        {
            if (P(g) is not { } p) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0)
            {
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                continue;
            }
            if (g.Events.Select(e => e.Payload).OfType<HandLimitDiscardedEvent>().Any()) return;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The discard phase never completed: " + P(g)?.Prompt);
    }

    // Mirrors the engine-side feasible enumeration and the counterpart AI
    // order: most cards for the owner, then the higher rank sum, then the
    // largest-mask equal-sum partner for themselves.
    private static (int[] Owner, int[] Chooser, int[] Leftover) ExpectedAiSplit(int[] ranks, int[] ids)
    {
        var total = (1 << ranks.Length) - 1;
        var ownerMask = Enumerable.Range(1, total - 1)
            .Where(mask => EqualSumExists(ranks, mask, total ^ mask))
            .OrderByDescending(mask => BitOperations.PopCount((uint)mask))
            .ThenByDescending(mask => RankSum(ranks, mask))
            .ThenBy(mask => "mask-" + mask, StringComparer.Ordinal)
            .FirstOrDefault(-1);
        if (ownerMask < 0) return ([], [], ids);
        var pool = total ^ ownerMask;
        var chooserMask = EnumerateSubmasks(pool)
            .Where(mask => RankSum(ranks, mask) == RankSum(ranks, ownerMask))
            .Max();
        return (IdsOf(ids, ownerMask), IdsOf(ids, chooserMask), IdsOf(ids, pool ^ chooserMask));
    }

    private static bool EqualSumExists(int[] ranks, int mask, int pool) =>
        EnumerateSubmasks(pool).Any(mask2 => RankSum(ranks, mask2) == RankSum(ranks, mask));

    private static IEnumerable<int> EnumerateSubmasks(int pool)
    {
        for (var subset = pool; subset > 0; subset = (subset - 1) & pool)
            yield return subset;
    }

    private static int RankSum(int[] ranks, int mask) =>
        Enumerable.Range(0, ranks.Length).Where(index => (mask & (1 << index)) != 0)
            .Sum(index => ranks[index]);

    private static int[] IdsOf(int[] ids, int mask) =>
        Enumerable.Range(0, ids.Length).Where(index => (mask & (1 << index)) != 0)
            .Select(index => ids[index]).ToArray();

    private static string Describe(IReadOnlyList<PlayerMarkerChangedEvent> trail) =>
        string.Join(";", trail.Select(m => m.Delta + "->" + m.Count));

    private static IReadOnlyList<Card> CardsAt(GameEngine g, CardLocation location) =>
        ((CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(g)!).CardsAt(location);

    private static (GameEngine, ContentRegistry) Start(string modeId, string generalId)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new ZhangChangPuFixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 24
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, generalId, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 ||
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") is Order or Hurt));
        return (g, r);
    }

    private sealed class ZhangChangPuFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zcp", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly
                    .GetType("CardGame.Content.Standard.OrdinaryZhangChangPuContent")!
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddSkill(new(Order, "排顶夹具", "fixture")
            {
                Program = Load(Order, "\"triggers\":[{\"id\":\"order\",\"window\":\"turnStartBeforeNormalFlow\"," +
                    "\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"reorderTopCards\",\"target\":\"owner\",\"amount\":16}]}]")
            });
            b.AddSkill(new(Hurt, "自伤夹具", "fixture")
            {
                Program = Load(Hurt, "\"triggers\":[{\"id\":\"hurt\",\"window\":\"turnStartBeforeNormalFlow\"," +
                    "\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]")
            });
            b.AddGeneral(new("fixture:zcp", "张昌蒲", "ol-zhang-chang-pu", Yanjiao, "wei", 3,
                new[] { Shengshen, Order }));
            b.AddGeneral(new("fixture:zcp-hurt", "张昌蒲", "ol-zhang-chang-pu", Yanjiao, "wei", 3,
                new[] { Shengshen, Hurt, Order }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:zcp-{i}", "fixture-peer-" + i, "supporter", "standard:none", "wei", 6, null));
            b.AddDeck(new("fixture:zcp-deck-split", "fixed", 4, 1, [])
            {
                PhysicalCards = CraftedDeck([.. Enumerable.Repeat(1, 16), .. Enumerable.Repeat(2, 16),
                    .. Enumerable.Repeat(4, 16), .. Enumerable.Repeat(7, 16)])
            });
            b.AddDeck(new("fixture:zcp-deck-nosplit", "fixed", 4, 1, [])
            {
                PhysicalCards = CraftedDeck([.. Enumerable.Repeat(1, 16), .. Enumerable.Repeat(2, 16),
                    .. Enumerable.Repeat(4, 16), .. Enumerable.Repeat(8, 16)])
            });
            b.AddDeck(new("fixture:zcp-deck-shen", "fixed", 4, 1, [])
            {
                PhysicalCards = CraftedDeck([.. Enumerable.Repeat(1, 11), .. Enumerable.Repeat(2, 11),
                    .. Enumerable.Repeat(3, 10), .. Enumerable.Repeat(4, 11), .. Enumerable.Repeat(6, 10),
                    .. Enumerable.Repeat(7, 11)])
            });
            foreach (var (deck, id) in new[]
                     {
                         ("fixture:zcp-deck-split", "identity:zcp-split"),
                         ("fixture:zcp-deck-nosplit", "identity:zcp-nosplit"),
                         ("fixture:zcp-deck-shen", "identity:zcp-hurt")
                     })
                b.AddMode(new(id, id, 4, 4,
                    new Dictionary<string, int>
                        { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                    deck, GeneralCandidateCount: 4,
                    GeneralPoolIds: ["fixture:zcp", "fixture:zcp-hurt", "fixture:zcp-1", "fixture:zcp-2", "fixture:zcp-3"]));
        }

        private static ContentDeckPhysicalCard[] CraftedDeck(int[] ranks) =>
            ranks.Select(rank => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, rank)).ToArray();

        private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load(
            "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
            ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}",
            "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    }
}
